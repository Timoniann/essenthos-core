using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Records;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record OccupationName(string Name, string Masculine, string Feminine, string? Description = null);

internal sealed record OccupationConcept(
    string Slug, string Category, IReadOnlyList<string> StrongNumbers, Dictionary<string, OccupationName> Labels);

/// <param name="Reference">The verse as <paramref name="Text"/> numbers it, <c>NEH 10:3</c>.</param>
/// <param name="Quote">The words as that text prints them in that verse, letter for letter.</param>
internal sealed record OccupationQuote(string Kind, string Text, string Reference, string Quote);

/// <param name="Source">Who read it and who decided it; the file's own where null.</param>
internal sealed record OccupationClaim(string Person, string Occupation, IReadOnlyList<OccupationQuote> Witnesses, string? Source = null);

internal sealed record OccupationFile(
    int Version, string Source, IReadOnlyList<OccupationConcept> Occupations, IReadOnlyList<OccupationClaim> Assertions);

/// <param name="Written">False where every file was current and nothing was read.</param>
internal sealed record OccupationOutcome(bool Written, int Occupations, int People, int Witnesses, int Words)
{
    public override string ToString() =>
        Written
            ? $"occupations: {Occupations} occupations, {People} people holding them, {Witnesses} quotations, {Words} words naming them"
            : "occupations: every file is current";
}

/// <summary>
/// What the text says people did or were, each with the verses that say it, from
/// <c>Essenthos/person-occupations.json</c>; and the words of those verses that name it. Written as the record files the
/// build loads: <c>records/facts/occupations</c> and their names in each language, <c>records/facts/person-occupations</c>,
/// <c>records/facts/occupation-witnesses</c> and <c>records/derived/occupation-words</c>, words named by key.
///
/// <para>
/// Every quotation is checked against the texts' words products: the verse is found in the text it names, as
/// that text numbers it, and the quotation must stand in it letter for letter. One that does not stops
/// the producer and names every such quotation, as a ruling whose word is not at its address does. The
/// verse's place in the shared frame is the one the text's frame gives it, so BHSA's <c>NEH 10:3</c> is
/// cited as the English 10:2.
/// </para>
///
/// <para>
/// The words marked are those of a quotation that carry one of the occupation's Strong numbers, and
/// in every other text the words the links of the pairs' finals say render one of them: a word of a Renders or Equals
/// link that carries the number itself, or that is the only word on its side of a link whose other
/// side is that word alone. A link that renders <em>the king</em> by <em>So the king</em> says which
/// words go together, not which of them is the king: there the word marked is the one carrying the
/// number, or the one the text most often renders the number by on its own (<em>king</em>). A pronoun
/// is never marked, and a word common in its text (<em>was</em>, <em>under</em>) only where the text
/// renders the number by it often. A Strong number counts only where the text's words may be served with it
/// (<see cref="StrongTerms"/>), as in the database.
/// </para>
/// </summary>
internal static partial class OccupationFiles
{
    public const string FileName = "person-occupations.json";

    public const string Producer = "forge occupations";

    public const string OccupationsProduct = "records/facts/occupations";

    public const string PeopleProduct = "records/facts/person-occupations";

    public const string WitnessesProduct = "records/facts/occupation-witnesses";

    public const string WordsProduct = "records/derived/occupation-words";

    internal static readonly string[] Languages = ["eng", "ukr", "deu", "spa"];

    internal static readonly string[] Categories =
        ["trade", "office", "military_role", "religious_role", "heavenly_role", "designation"];

    public static string Source => $"{ProductStore.EssenthosFolder}/{FileName}";

    public static string LabelsProduct(string language) => $"records/i18n/{language}/occupations";

    private static readonly JsonSerializerOptions Shape = new(JsonSerializerDefaults.Web);

    public static OccupationFile Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<OccupationFile>(stream, Shape) ?? throw new InvalidDataException($"{path} is empty.");
    }

    internal static void Validate(OccupationFile file)
    {
        if (file.Version != 1 || string.IsNullOrWhiteSpace(file.Source) || file.Occupations.Count == 0)
        {
            throw new InvalidDataException("Occupations need version 1, a source and at least one occupation.");
        }

        var slugs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var concept in file.Occupations)
        {
            if (!SlugShape().IsMatch(concept.Slug) || !slugs.Add(concept.Slug) || !Categories.Contains(concept.Category)
                || concept.StrongNumbers.Any(number => StrongNumbers.Normalize(number) != number)
                || concept.Labels.Count != Languages.Length
                || Languages.Any(language => !concept.Labels.TryGetValue(language, out var label)
                                             || string.IsNullOrWhiteSpace(label.Name)
                                             || string.IsNullOrWhiteSpace(label.Masculine)
                                             || string.IsNullOrWhiteSpace(label.Feminine)))
            {
                throw new InvalidDataException(
                    $"Occupation {concept.Slug} needs a unique slug, a known category, plain Strong numbers and its name in {string.Join(", ", Languages)}.");
            }
        }

        var pairs = new HashSet<(string, string)>();
        foreach (var claim in file.Assertions)
        {
            if (!slugs.Contains(claim.Occupation) || !pairs.Add((claim.Person, claim.Occupation))
                || claim.Witnesses.Any(w => w.Kind == Profession) == claim.Witnesses.Any(w => w.Kind == OwnBook)
                || claim.Witnesses.Any(w => w.Kind is not (Profession or Identity or OwnBook) || string.IsNullOrWhiteSpace(w.Quote)
                                            || Address(w.Reference) is null))
            {
                throw new InvalidDataException(
                    $"{claim.Person} as {claim.Occupation}: each person is said once per occupation, by a quotation saying it or else by their own book, never by both, each at a verse.");
            }
        }
    }

    /// <summary>
    /// Writes the occupation files whose inputs moved (every one when forced): the file and the records decide the
    /// occupations and who holds them; the quoted texts' words and frames the quotations; and every pair of a quoted
    /// text, with its other text's words, declaration and Strong index, the words that name them.
    /// </summary>
    public static OccupationOutcome Write(ProductStore store, bool force)
    {
        var path = store.FullPath(Source);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The occupations are read from {path}, which is not there; it is carried in git.", path);
        }

        var file = Read(path);
        Validate(file);
        var quotedTexts = file.Assertions.SelectMany(a => a.Witnesses).Select(w => w.Text).Distinct().Order(StringComparer.Ordinal).ToList();
        var pairs = MappingProducts.Pairs(store)
            .Where(pair => store.Exists(MappingProducts.Of(pair, MappingProducts.Final)))
            .Where(pair => MappingProducts.Split(pair) is var (from, to) && (quotedTexts.Contains(from) || quotedTexts.Contains(to)))
            .ToList();
        var linkedTexts = pairs.SelectMany(pair => MappingProducts.Split(pair) is var (from, to) ? new[] { from, to } : [])
            .Concat(quotedTexts).Distinct().Order(StringComparer.Ordinal).ToList();

        IReadOnlyList<ProductInput> Inputs(IEnumerable<string> products) =>
            store.Inputs(products.Select(store.Find).OfType<string>().Prepend(Source).Distinct().Order(StringComparer.Ordinal));

        string[] records = ["records/records", "records/folds"];
        var quotedProducts = quotedTexts.SelectMany(t => new[] { TextWord.ProductOf(t), FrameFile.Product(t) }).ToList();
        var own = Inputs([]);
        var held = Inputs(records);
        var quoted = Inputs([.. records, .. quotedProducts]);
        var marked = Inputs(
        [
            .. records, .. quotedProducts,
            .. pairs.Select(pair => MappingProducts.Of(pair, MappingProducts.Final)),
            .. linkedTexts.SelectMany(t => new[] { TextWord.ProductOf(t), Conversion.TextField.ProductOf(t), IndexProducts.Of(t, IndexProducts.Strong) }),
        ]);
        var files = new List<(RecordFile File, IReadOnlyList<ProductInput> Inputs)> { (RecordProducers.File(OccupationsProduct), own) };
        files.AddRange(Languages.Select(language => (RecordProducers.File(LabelsProduct(language)), own)));
        files.Add((RecordProducers.File(PeopleProduct), held));
        files.Add((RecordProducers.File(WitnessesProduct), quoted));
        files.Add((RecordProducers.File(WordsProduct), marked));
        if (!force && files.All(f => !store.NeedsBuild(f.File.Product, f.File.Kind, f.Inputs)))
        {
            return new OccupationOutcome(false, 0, 0, 0, 0);
        }

        var people = People(store, file);
        var claims = file.Assertions.Select(claim => (Person: people[claim.Person], Claim: claim)).ToList();
        var twice = claims.GroupBy(c => (c.Person, c.Claim.Occupation)).FirstOrDefault(g => g.Count() > 1);
        if (twice is not null)
        {
            throw new InvalidDataException(
                $"{string.Join(" and ", twice.Select(c => c.Claim.Person))} are one record, {twice.Key.Person}, said twice to be {twice.Key.Occupation}.");
        }

        var found = Find(store, file);
        var words = MarkTheWords(store, file, claims, found, pairs);

        var rows = new Dictionary<string, IEnumerable<string?[]>>(StringComparer.Ordinal)
        {
            [OccupationsProduct] = file.Occupations.Select(o => new string?[] { o.Slug, o.Category, string.Join(' ', o.StrongNumbers) }),
            [PeopleProduct] = claims.Select(c => new string?[] { c.Person, c.Claim.Occupation, c.Claim.Source ?? file.Source }),
            [WitnessesProduct] = claims.SelectMany(c => c.Claim.Witnesses.Distinct().Select((w, i) => new string?[]
            {
                c.Person, c.Claim.Occupation, Integer(i + 1), w.Kind, w.Text, w.Reference, found[w].Canonical.ToString(), w.Quote,
            })),
            [WordsProduct] = words.Select(w => new string?[] { w.Person, w.Occupation, w.Word.Text, w.Word.Own }),
        };
        foreach (var language in Languages)
        {
            rows[LabelsProduct(language)] = file.Occupations.Select(o => o.Labels[language] is var label
                ? new string?[] { o.Slug, label.Name, label.Masculine, label.Feminine, label.Description }
                : []);
        }

        foreach (var (recordFile, inputs) in files)
        {
            RecordProducers.Write(store, recordFile, rows[recordFile.Product], Producer, inputs);
        }

        return new OccupationOutcome(true, file.Occupations.Count, claims.Count, claims.Sum(c => c.Claim.Witnesses.Distinct().Count()), words.Count);
    }

    private const string Profession = "profession";
    private const string Identity = "identity";

    /// <summary>A prophet whom no text calls one: the claim rests on the book that bears his name.</summary>
    private const string OwnBook = "own-book";

    /// <summary>The people by the slug the file names them by, a folded record's slug reaching the kept one.</summary>
    private static Dictionary<string, string> People(ProductStore store, OccupationFile file)
    {
        var slugs = file.Assertions.Select(a => a.Person).ToHashSet(StringComparer.Ordinal);
        var persons = Rows(store, "records/records").Where(r => r[1] == EnumSpelling.Of(EntityKind.Person)).Select(r => r[0]!)
            .ToHashSet(StringComparer.Ordinal);
        var people = slugs.Where(persons.Contains).ToDictionary(slug => slug, slug => slug, StringComparer.Ordinal);
        foreach (var fold in Rows(store, "records/folds").Where(r => slugs.Contains(r[0]!) && persons.Contains(r[1]!)))
        {
            people.TryAdd(fold[0]!, fold[1]!);
        }

        var unknown = slugs.Where(slug => !people.ContainsKey(slug)).Order(StringComparer.Ordinal).ToList();
        return unknown.Count == 0
            ? people
            : throw new InvalidDataException($"Occupations name {unknown.Count} people the records do not hold: {string.Join(", ", unknown.Take(20))}.");
    }

    private static List<string?[]> Rows(ProductStore store, string product)
    {
        using var reader = store.Open(product, RecordProducers.File(product).Schema());
        return [.. reader.ReadAll()];
    }

    /// <summary>A word of a quotation: its key, and the Strong number the database would serve on it.</summary>
    private sealed record QuotedWord(WordKey Key, string? StrongNumber);

    /// <summary>Where a quotation stands: its verse's place in the frame and the words it spans.</summary>
    private sealed record Quoted(VerseKey Canonical, IReadOnlyList<QuotedWord> Words);

    /// <summary>
    /// Every quotation found in its text: its verse, numbered as that text numbers it (of two verses with one number,
    /// the first by letter that holds it), and the words its letters fall on. A quotation that is not there stops the
    /// producer.
    /// </summary>
    private static Dictionary<OccupationQuote, Quoted> Find(ProductStore store, OccupationFile file)
    {
        var quotes = file.Assertions.SelectMany(a => a.Witnesses).Distinct().ToList();
        var verses = new ConcurrentDictionary<string, Dictionary<(int Book, int Chapter, int Verse), SortedDictionary<string, List<Word>>>>();
        var frames = new ConcurrentDictionary<string, Dictionary<VerseKey, VerseKey>>();
        Parallel.ForEach(quotes.GroupBy(q => q.Text), group =>
        {
            if (store.Find(TextWord.ProductOf(group.Key)) is null)
            {
                return;
            }

            var keys = group.Select(q => Address(q.Reference)!.Value).ToHashSet();
            var held = new Dictionary<(int, int, int), SortedDictionary<string, List<Word>>>();
            foreach (var word in Words(store, group.Key, surface: true))
            {
                var at = (word.Key.Verse.Book, word.Key.Verse.Chapter, word.Key.Verse.Verse);
                if (word.Key.Verse.ChapterLabel.Length > 0 || !keys.Contains(at))
                {
                    continue;
                }

                var labelled = held.TryGetValue(at, out var known) ? known : held[at] = new SortedDictionary<string, List<Word>>(StringComparer.Ordinal);
                (labelled.TryGetValue(word.Key.Verse.VerseLabel, out var list) ? list : labelled[word.Key.Verse.VerseLabel] = []).Add(word);
            }

            verses[group.Key] = held;
            frames[group.Key] = Frame(store, group.Key);
        });

        var found = new Dictionary<OccupationQuote, Quoted>();
        var missing = new List<string>();
        foreach (var quote in quotes)
        {
            var key = Address(quote.Reference)!.Value;
            Quoted? hit = null;
            if (verses.TryGetValue(quote.Text, out var held) && held.TryGetValue(key, out var labelled))
            {
                foreach (var (label, spelled) in labelled)
                {
                    spelled.Sort((a, b) => a.Key.Position.CompareTo(b.Key.Position));
                    var line = string.Concat(spelled.Select(w => w.Surface + w.Trailer));
                    var at = line.IndexOf(quote.Quote, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        continue;
                    }

                    var end = at + quote.Quote.Length;
                    var spanned = new List<QuotedWord>();
                    var offset = 0;
                    foreach (var word in spelled)
                    {
                        if (offset < end && offset + word.Surface!.Length > at)
                        {
                            spanned.Add(new QuotedWord(word.Key, word.Strong));
                        }

                        offset += word.Surface!.Length + word.Trailer!.Length;
                    }

                    var verse = new VerseKey(key.Book, key.Chapter, key.Verse, label);
                    hit = new Quoted(frames[quote.Text].TryGetValue(verse, out var placed) ? placed : FrameFile.AtOwn(verse), spanned);
                    break;
                }
            }

            if (hit is null)
            {
                missing.Add($"{quote.Text} {quote.Reference} «{quote.Quote}»");
            }
            else
            {
                found[quote] = hit;
            }
        }

        return missing.Count == 0
            ? found
            : throw new InvalidDataException(
                $"{missing.Count} occupation quotations are not in their verse: {string.Join("; ", missing.Take(20))}.");
    }

    /// <summary>Each verse of a text the frame moves, at its primary place.</summary>
    private static Dictionary<VerseKey, VerseKey> Frame(ProductStore store, string text)
    {
        var placed = new Dictionary<VerseKey, VerseKey>();
        if (store.Exists(FrameFile.Product(text)))
        {
            using var reader = store.Open(FrameFile.Product(text), FrameRow.Schema);
            foreach (var row in reader.Rows())
            {
                foreach (var (own, canonical) in row.Verses())
                {
                    placed[own] = canonical[0];
                }
            }
        }

        return placed;
    }

    /// <summary>A word of a pair's link, as the marking reads it.</summary>
    private sealed record Member(WordKey Key, bool From);

    /// <summary>A word of another text a link gives with an occupation's word, and whether it is the link's only word.</summary>
    private sealed record Candidate(int Claim, WordKey Word, string? Form, bool Alone);

    /// <summary>
    /// The words that name each occupation in the verses that say it: the quotation's words carrying one of its numbers,
    /// and the words of other texts the links say render them, each with the person and the occupation.
    /// </summary>
    private static List<(string Person, string Occupation, WordKey Word)> MarkTheWords(
        ProductStore store,
        OccupationFile file,
        List<(string Person, OccupationClaim Claim)> claims,
        Dictionary<OccupationQuote, Quoted> quoted,
        List<string> pairs)
    {
        var concepts = file.Occupations.ToDictionary(o => o.Slug, StringComparer.Ordinal);
        var naming = new Dictionary<WordKey, HashSet<int>>();
        var numbersOf = new Dictionary<int, HashSet<string>>();
        for (var c = 0; c < claims.Count; c++)
        {
            var concept = concepts[claims[c].Claim.Occupation];
            if (concept.StrongNumbers.Count == 0)
            {
                continue;
            }

            numbersOf[c] = [.. concept.StrongNumbers];
            foreach (var word in claims[c].Claim.Witnesses.Where(w => w.Kind == Profession).SelectMany(w => quoted[w].Words)
                         .Where(w => StrongNumbers.Normalize(w.StrongNumber) is { } number && concept.StrongNumbers.Contains(number)))
            {
                (naming.TryGetValue(word.Key, out var claimsOf) ? claimsOf : naming[word.Key] = []).Add(c);
            }
        }

        var wanted = naming.SelectMany(pair => pair.Value.Select(c => (Claim: c, Word: pair.Key))).ToHashSet();
        var seeds = naming.Keys.GroupBy(k => k.Text).ToDictionary(g => g.Key, g => g.Select(k => k.Own).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

        // Every Renders or Equals link of a pair holding a seed, once per seed it holds, with its words on both sides.
        var sides = new ConcurrentBag<(WordKey Seed, List<Member> Link)>();
        Parallel.ForEach(pairs.Where(pair => MappingProducts.Split(pair) is var (from, to) && (seeds.ContainsKey(from) || seeds.ContainsKey(to))),
            pair =>
            {
                var (fromText, toText) = MappingProducts.Split(pair);
                var fromSeeds = seeds.GetValueOrDefault(fromText);
                var toSeeds = seeds.GetValueOrDefault(toText);
                foreach (var (from, to) in Links(store, pair))
                {
                    var fromKeys = MappingText.Split(from).ToList();
                    var toKeys = MappingText.Split(to).ToList();
                    var held = (fromSeeds is not null ? fromKeys.Where(fromSeeds.Contains).Select(k => WordKey.Parse(k, fromText)) : [])
                        .Concat(toSeeds is not null ? toKeys.Where(toSeeds.Contains).Select(k => WordKey.Parse(k, toText)) : []).ToList();
                    if (held.Count == 0)
                    {
                        continue;
                    }

                    List<Member> link =
                    [
                        .. fromKeys.Select(k => new Member(WordKey.Parse(k, fromText), true)),
                        .. toKeys.Select(k => new Member(WordKey.Parse(k, toText), false)),
                    ];
                    foreach (var seed in held)
                    {
                        sides.Add((seed, link));
                    }
                }
            });

        var others = Lookup(store, sides.SelectMany(s => s.Link.Select(m => m.Key)));
        var candidates = new List<Candidate>();
        foreach (var (seed, link) in sides)
        {
            var side = link.Single(m => m.Key == seed).From;
            var across = link.Where(m => m.From != side).ToList();
            var single = across.Count == 1 && link.Count(m => m.From == side) == 1;
            foreach (var c in naming[seed])
            {
                foreach (var other in across)
                {
                    var word = others.Words[other.Key];
                    if (StrongNumbers.Normalize(word.Strong) is { } number && numbersOf[c].Contains(number))
                    {
                        wanted.Add((c, other.Key));
                    }
                    else
                    {
                        candidates.Add(new Candidate(c, other.Key, word.Normalised, single));
                    }
                }
            }
        }

        foreach (var candidate in Saying(store, candidates, numbersOf, others))
        {
            wanted.Add((candidate.Claim, candidate.Word));
        }

        return [.. wanted.Select(w => (claims[w.Claim].Person, claims[w.Claim].Claim.Occupation, w.Word))];
    }

    /// <summary>The words of a pair's standing Renders and Equals links, each link once, by its settled claim.</summary>
    private static IEnumerable<(string From, string To)> Links(ProductStore store, string pair)
    {
        var product = MappingProducts.Of(pair, MappingProducts.Final);
        var relative = store.Find(product)!;
        if (relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, ["from", "to", "relation", "settled"]);
                var (from, to, relation, settled) = (group.Column("from"), group.Column("to"), group.Column("relation"), group.Column("settled"));
                for (var r = 0; r < group.Rows; r++)
                {
                    if (settled.Boolean(r) == true && relation.Text(r) is "renders" or "equals")
                    {
                        yield return (from.Text(r)!, to.Text(r)!);
                    }
                }
            }

            yield break;
        }

        using var reader = store.Open(product, FinalRow.Schema);
        foreach (var row in reader.Rows().Where(r => r.Settled && r.Relation is "renders" or "equals"))
        {
            yield return (row.From, row.To);
        }
    }

    /// <summary>A word as the marking reads it; a field the read left out is null.</summary>
    private sealed record Word(WordKey Key, string? Surface, string? Trailer, string? Strong, string? Normalised);

    /// <summary>
    /// The words of a text in the product's order, with the Strong number only where the text's numbers may be served
    /// (<see cref="TextWordLoad.StrongServed"/>) and its letters only when <paramref name="surface"/> asks for them.
    /// </summary>
    private static IEnumerable<Word> Words(ProductStore store, string text, bool surface)
    {
        var relative = store.Find(TextWord.ProductOf(text)) ?? throw new FileNotFoundException($"The store holds no words of {text}.");
        var served = TextWordLoad.StrongServed(store, text);
        if (relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            string[] columns = surface ? ["own", "surface", "trailer", "strong", "normalised"] : ["own", "strong", "normalised"];
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, columns);
                var (own, strong, normalised) = (group.Column("own"), group.Column("strong"), group.Column("normalised"));
                var letters = surface ? group.Column("surface") : null;
                var trailer = surface ? group.Column("trailer") : null;
                for (var r = 0; r < group.Rows; r++)
                {
                    yield return new Word(own.WordKey(r, text)!.Value, letters?.Text(r), trailer?.Text(r), served ? strong.Text(r) : null,
                        normalised.Text(r));
                }
            }

            yield break;
        }

        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        foreach (var word in reader.Rows())
        {
            yield return new Word(word.Key, word.Surface, word.Trailer, served ? word.Strong : null, word.Normalised);
        }
    }

    /// <summary>The words asked for, and how often each text prints each form, read once per text.</summary>
    private sealed record Looked(IReadOnlyDictionary<WordKey, Word> Words, IReadOnlyDictionary<string, Dictionary<string, int>> Forms);

    private static Looked Lookup(ProductStore store, IEnumerable<WordKey> keys)
    {
        var asked = keys.GroupBy(k => k.Text).ToDictionary(g => g.Key, g => g.ToHashSet(), StringComparer.Ordinal);
        var words = new ConcurrentDictionary<WordKey, Word>();
        var forms = new ConcurrentDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        Parallel.ForEach(asked, text =>
        {
            var counted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var word in Words(store, text.Key, surface: false))
            {
                if (text.Value.Contains(word.Key))
                {
                    words[word.Key] = word;
                }

                if (word.Normalised is { } form)
                {
                    counted[form] = counted.GetValueOrDefault(form) + 1;
                }
            }

            forms[text.Key] = counted;
        });

        var missing = asked.SelectMany(t => t.Value).Where(k => !words.ContainsKey(k)).Take(5).ToList();
        return missing.Count == 0
            ? new Looked(words, forms)
            : throw new InvalidDataException($"A final names words its text does not hold: {string.Join(", ", missing)}.");
    }

    /// <summary>A word as common as this in its text is a function word unless the text renders the number by it often.</summary>
    private const int Common = 300;

    /// <summary>How often a text must render a number by a common word to keep it.</summary>
    private const int Often = 10;

    /// <summary>How many of a number's commonest renderings in a text count as its words there.</summary>
    private const int Commonest = 3;

    /// <summary>
    /// The words of other texts that say the occupation, among those the links give with its word. A
    /// pronoun never does. The only word of a link does, unless it is a word common in its text
    /// (<em>was</em>, <em>under</em>), where an aligner lands when it has nothing better; a common
    /// word is kept where it is among the text's commonest renderings of the number, as the commonest
    /// occupations' words are (<em>king</em>, <em>цар</em>). Of several words a link gives together
    /// (<em>the king</em>), the one that is itself among the commonest renderings of the number in
    /// its text does.
    /// </summary>
    private static List<Candidate> Saying(
        ProductStore store,
        List<Candidate> candidates,
        Dictionary<int, HashSet<string>> numbersOf,
        Looked looked)
    {
        candidates = [.. candidates.Where(c => c.Form is not null)];
        var numbers = numbersOf.Values.SelectMany(n => n).ToHashSet(StringComparer.Ordinal);
        var texts = candidates.Select(c => c.Word.Text).Distinct().ToList();
        var languages = texts.ToDictionary(text => text, text => Language(store, text), StringComparer.Ordinal);
        var uses = Renderings(store, texts, numbers);

        int Rendered(Candidate c) =>
            numbersOf[c.Claim].Select(number => uses.GetValueOrDefault((c.Word.Text, number, c.Form!))).DefaultIfEmpty(0).Max();

        return
        [
            .. candidates.Where(c =>
            {
                if (Pronouns.Is(languages[c.Word.Text] ?? "", c.Form!))
                {
                    return false;
                }

                var common = looked.Forms[c.Word.Text].GetValueOrDefault(c.Form!) >= Common;
                var rendered = Rendered(c);
                return c.Alone ? !common || rendered >= Often : rendered > 0 && (!common || rendered >= Often);
            }),
        ];
    }

    private static string? Language(ProductStore store, string text)
    {
        using var reader = store.Open(Conversion.TextField.ProductOf(text), Conversion.TextField.Schema);
        return reader.Rows().FirstOrDefault(f => f.Field == "language")?.Value;
    }

    /// <summary>
    /// How often each text renders each of the numbers by each of its commonest phrases, lower-cased, as
    /// <c>strong_rendering</c> holds them: the phrases its Strong index gives, ranked by uses and then by phrase as the
    /// database collates it (<see cref="TextIndexes.Collated"/>), the first <see cref="Commonest"/> used at least twice.
    /// </summary>
    private static Dictionary<(string Text, string Number, string Phrase), int> Renderings(
        ProductStore store, IReadOnlyList<string> texts, IReadOnlySet<string> numbers)
    {
        var uses = new ConcurrentDictionary<(string, string, string), int>();
        Parallel.ForEach(texts.Where(t => store.Exists(IndexProducts.Of(t, IndexProducts.Strong))), text =>
        {
            var counted = new Dictionary<(string Number, string Phrase), int>();
            using (var reader = store.Open(IndexProducts.Of(text, IndexProducts.Strong), StrongIndexRow.Schema))
            {
                foreach (var row in reader.Rows().Where(r => r.Phrase is not null && numbers.Contains(r.Number)))
                {
                    counted[(row.Number, row.Phrase!)] = counted.GetValueOrDefault((row.Number, row.Phrase!)) + 1;
                }
            }

            foreach (var number in counted.GroupBy(c => c.Key.Number))
            {
                var ranked = number.OrderByDescending(c => c.Value)
                    .ThenBy(c => c.Key.Phrase, Comparer<string>.Create(TextIndexes.Collated))
                    .Take(Commonest);
                foreach (var ((_, phrase), count) in ranked.Where(c => c.Value >= 2))
                {
                    uses[(text, number.Key, phrase.ToLowerInvariant())] = count;
                }
            }
        });

        return new Dictionary<(string, string, string), int>(uses);
    }

    /// <summary><c>NEH 10:3</c> as book, chapter and verse; a psalm's title is its verse 0.</summary>
    internal static (int Book, int Chapter, int Verse)? Address(string? reference)
    {
        var match = reference is null ? null : ReferenceShape().Match(reference);
        return match is { Success: true }
               && BookReferences.ResolveOrdinal(match.Groups[1].Value) is { } book
               && int.TryParse(match.Groups[2].ValueSpan, out var chapter) && chapter > 0
               && int.TryParse(match.Groups[3].ValueSpan, out var verse) && verse >= 0
            ? (book, chapter, verse)
            : null;
    }

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^([1-3]?[A-Z]{2,3}) (\d{1,3}):(\d{1,3})$")]
    private static partial Regex ReferenceShape();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugShape();
}
