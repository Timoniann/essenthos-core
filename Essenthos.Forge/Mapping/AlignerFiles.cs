using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Configuration;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Indexes;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using GeezTextSource = Essenthos.Core.Loading.GeezTextSource;
using TextField = Essenthos.Core.Conversion.TextField;
using Word = Essenthos.Core.Loading.Links.AlignmentPipeline.Word;
using WordForms = Essenthos.Core.Loading.Links.AlignmentPipeline.WordForms;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Every word of a text by the frame address it stands at, as SIL's aligner reads it, from the text's words, verses and
/// frame: what <see cref="AlignmentPipeline"/> read from a database, in the same order, so that the lines written for the
/// aligner are the ones its stored answers were made from.
/// </summary>
internal static class AlignedWords
{
    private sealed record Features(string? Consonantal, string? Pos, string? Form, string? Robinson);

    private static readonly ConditionalWeakTable<ProductText, Features[]> Parsed = new();

    /// <param name="form">The token a word is written as (<see cref="AlignmentPipeline.Reduce"/>, ...).</param>
    /// <param name="lettered">Whether the verses the text prints with a letter are read: only against a text numbered in the same tradition.</param>
    /// <param name="primaryOnly">Every word at its verse's primary address alone, as a pool reads its texts.</param>
    /// <param name="books">The canonical books to read, or all of them.</param>
    /// <param name="strong">Whether the words carry their Strong numbers: only where the words product lets them be served.</param>
    /// <param name="asides">Whether a word read at a further address its verse covers says where the verse stands.</param>
    /// <param name="hebrew">
    /// Where lettered verses are not read, the addresses a verse of the Hebrew stands at: the lettered verses that are one of
    /// them (<see cref="HebrewLettered"/>) are read there all the same.
    /// </param>
    public static Dictionary<(int, int, int), List<Word>> Read(
        ProductText text,
        Func<WordForms, string> form,
        bool lettered,
        bool primaryOnly = false,
        IReadOnlySet<int>? books = null,
        bool strong = true,
        bool asides = true,
        IReadOnlySet<(int, int, int)>? hebrew = null)
    {
        var features = Parsed.GetValue(text, Parse);
        var asHebrew = lettered || hebrew is null ? null : HebrewLettered(text, hebrew);
        var verses = text.Verses.Where(verse => lettered || verse.VerseLabel.Length == 0 || asHebrew?.Contains(verse) == true)
            .Where(verse => books is null || text.Addresses(verse).Any(address => books.Contains(address.Book)))
            .ToList();
        var uncapitalised = verses.SelectMany(text.InVerse)
            .Select(id => text.Words[id])
            .Where(word => word.Key.Position > 1 && word.Surface.Length > 0 && char.IsLower(word.Surface[0]))
            .Select(word => word.Surface.ToLowerInvariant())
            .ToHashSet();

        // A text with a map of its own is read where it stands primarily, and a mapped verse where the map reads it and
        // nowhere else; one the map has no line for stands nowhere, since its own number is some other passage's address.
        var mapped = text.Slug == GeezTextSource.Slug;
        var placed = new Dictionary<(int, int, int), List<(VerseKey Verse, (int, int, int)? Aside)>>();
        foreach (var verse in verses)
        {
            var primary = text.Primary(verse);
            IEnumerable<(int, int, int)> addresses = mapped
                ? !GeezVerseMap.Books.Contains(verse.Book)
                    ? [primary]
                    : GeezVerseMap.Addresses.TryGetValue((verse.Book, verse.Chapter, verse.Verse), out var at) ? at : []
                : primaryOnly
                    ? [primary]
                    : text.Addresses(verse).Select(address => (address.Book, address.Chapter, address.Verse)).Distinct();
            foreach (var address in addresses)
            {
                if (!mapped && books is not null && !books.Contains(address.Item1))
                {
                    continue;
                }

                if (!placed.TryGetValue(address, out var standing))
                {
                    placed[address] = standing = [];
                }

                standing.Add((verse, asides && !mapped && address != primary ? primary : null));
            }
        }

        // Where several verses meet at one address each is read whole, in the order the edition prints them. Ordered by
        // position alone, the twenty-five verses Swete prints at 3 Kingdoms 12:24 were read as the first word of each, then
        // the second of each, and a model learns where a word stands from exactly that order.
        var sequence = text.Sequence;
        return placed.ToDictionary(
            entry => entry.Key,
            entry => entry.Value
                .OrderBy(standing => standing.Verse.Book)
                .ThenBy(standing => standing.Verse.Chapter)
                .ThenBy(standing => sequence.GetValueOrDefault(standing.Verse))
                .SelectMany(standing => text.InVerse(standing.Verse).Select(id => Word(text, id, features[id], form, uncapitalised, strong, standing.Aside)))
                .ToList());
    }

    /// <summary>
    /// The lettered verses of a text that are a verse of the Hebrew under the edition's letter: the frame stands each first at a
    /// Hebrew address not its own, and no verse of the edition without a letter stands at that address (Brenton's Proverbs
    /// 24:22f-t are the Hebrew's 30:1-14, his Joshua 9:2a-f its 8:30-35). A lettered verse at a numbered verse's address is the
    /// Greek's own addition beside that verse, whose words the Hebrew verse does not hold: Joshua 24:31a's knives of stone beside
    /// the burial, Psalm 144:13a's line beside the Hebrew's 145:13, Joshua 19:47a and 19:48a beside the inheritance of Dan.
    /// </summary>
    public static HashSet<VerseKey> HebrewLettered(ProductText text, IReadOnlySet<(int, int, int)> hebrew)
    {
        var numbered = text.Verses.Where(verse => verse.VerseLabel.Length == 0)
            .SelectMany(text.Addresses)
            .Select(address => (address.Book, address.Chapter, address.Verse))
            .ToHashSet();
        return
        [
            .. text.Verses.Where(verse => verse.VerseLabel.Length > 0
                                          && text.Primary(verse) is var primary
                                          && primary != (verse.Book, verse.Chapter, verse.Verse)
                                          && hebrew.Contains(primary)
                                          && !numbered.Contains(primary)),
        ];
    }

    /// <summary>
    /// Whether a text's frame stands a lettered verse first at an address not its own, read from the frame file alone: what makes
    /// the addresses of the Hebrew decide which of its lettered verses are read (<see cref="HebrewLettered"/>).
    /// </summary>
    public static bool PlacesLettered(ProductStore store, string slug)
    {
        if (!store.Exists(FrameFile.Product(slug)))
        {
            return false;
        }

        using var reader = store.Open(FrameFile.Product(slug), FrameRow.Schema);
        return reader.Rows().SelectMany(row => row.Verses())
            .Any(verse => verse.Own.VerseLabel.Length > 0 && verse.Canonical.Count > 0 && verse.Canonical[0] != FrameFile.AtOwn(verse.Own));
    }

    private static Word Word(ProductText text, int id, Features features, Func<WordForms, string> form, IReadOnlySet<string> uncapitalised,
        bool strong, (int, int, int)? aside)
    {
        var word = text.Words[id];
        var number = strong ? word.Strong : null;
        var forms = new WordForms(word.Surface, word.Lemma, features.Consonantal, number, text.Language, word.Key.Position);
        var letters = NameLists.Skeleton(features.Consonantal ?? word.Surface, text.Language);
        return new Word(
            id,
            AlignmentTokens.One(form(forms)),
            number,
            letters,
            AlignmentPipeline.IsNamed(forms, features.Pos, features.Form ?? features.Robinson, uncapitalised) && letters.Length > 0 ? letters : null,
            aside);
    }

    private static Features[] Parse(ProductText text)
    {
        var features = new Features[text.Words.Count];
        for (var id = 0; id < features.Length; id++)
        {
            features[id] = new Features(null, null, null, null);
            if (text.Words[id].Morphology is not { } morphology)
            {
                continue;
            }

            using var json = JsonDocument.Parse(morphology);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                features[id] = new Features(Field(json.RootElement, "consonantal"), Field(json.RootElement, "pos"), Field(json.RootElement, "form"),
                    Field(json.RootElement, "robinson"));
            }
        }

        return features;
    }

    private static string? Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

/// <summary>
/// SIL's aligner over the store's files (DOC-0224 §6.1, D1): the two texts' words written as the aligner's lines, the
/// answer stored for exactly those lines read from <c>Resources/Alignments</c> (trained only where the run may), and what
/// survives of it as rows of a method file. A word's id is its place in its own text (<see cref="ProductText"/>).
/// </summary>
internal sealed class AlignerFiles(ProductTexts texts, AlignmentPipeline pipeline)
{
    private static readonly ConditionalWeakTable<ProductStore, Lazy<SyntaxPrior>> Syntaxes = new();

    public ProductTexts Texts => texts;

    /// <summary>The aligner itself, which trains and reads the stored answers.</summary>
    public AlignmentPipeline Pipeline => pipeline;

    /// <summary>The store's alignments: one workspace per pair, under <c>Resources/Alignments</c>.</summary>
    public string Root => Path.Combine(texts.Store.ResourcesPath, AlignmentStore.FolderName);

    /// <summary>
    /// The answer files a workspace holds, relative to Resources: the one each set of inputs was trained into, and the one
    /// trained before answers were keyed. What a producer reading the workspace names among its inputs.
    /// </summary>
    public static IEnumerable<string> StoredAnswers(ProductStore store, string workspace)
    {
        var folder = Path.Combine(store.ResourcesPath, AlignmentStore.FolderName, workspace);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "pharaoh.txt", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(store.ResourcesPath, file).Replace('\\', '/'))
            .Where(file => file.EndsWith("/" + AlignmentPipeline.AlignmentFile.Replace('\\', '/'), StringComparison.Ordinal)
                           && !file.Contains(".training-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// A pair aligned and read at a threshold: the rows of its <c>aligner</c> file (each word pair the model names at or above
    /// the threshold, after the collapse, name and verse checks of <see cref="AlignmentPipeline.Read"/>), and what the run did.
    /// </summary>
    public async Task<(List<MappingRow> Rows, AlignmentOutcome Outcome)> Align(AlignedPair pair, CancellationToken cancellationToken = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        var from = texts[pair.From];
        var to = texts[pair.To];
        var measured = AlignmentPipeline.Measured.GetValueOrDefault(from.Language);
        var threshold = pair.Minimum ?? measured?.Minimum ?? AlignmentPipeline.DefaultMinimumConfidence;
        var pool = AlignmentPipeline.Pool(pair.From, pair.To);
        double? agreedBelow = measured is { AgreedBelow: { } measuredBelow } && threshold == measured.Minimum ? measuredBelow : null;
        var answer = await Answer(pair.From, pair.To, $"{pair.From}-{pair.To}", pair.Model, asWritten: false, books: null,
            lettered: pair.Outside is not null || SameVersification(pair.From, pair.To), cancellationToken, agreed: agreedBelow is not null);
        var prior = Syntax(pair.To);
        var agreement = agreedBelow is { } agreedFrom
            ? new AlignmentPipeline.Agreement(AlignmentPipeline.PairsOf(answer.Agreed!, answer.Addresses.Count), agreedFrom)
            : null;

        var (drafts, proposed, collapsed, below) = AlignmentPipeline.Read(
            answer.File, answer.Addresses, answer.Source, answer.Target, threshold, Selection.BestPerSource, prior,
            AlignmentPipeline.MarksNoNames(from.Language), AlignmentPipeline.BothMarkTheirNames(from.Language, to.Language), agreement);
        if (pair.Outside is { } outside)
        {
            var beyond = Beyond(from, texts[outside]);
            drafts = [.. drafts.Where(draft => beyond.Contains(draft.SourceWordId))];
        }

        var note = pool is null ? null : $"one model over {string.Join(", ", pool)}";
        if (measured is not null && threshold == measured.Minimum)
        {
            note = note is null ? measured.Note : $"{note}; {measured.Note}";
        }

        var rejected = Rejected(pair.From, pair.To);
        drafts.RemoveAll(draft => rejected.Contains((draft.SourceWordId, draft.TargetWordId)));

        // The model reports how likely the word pairing is and how likely the position is. A row has one confidence, so the
        // pairing is what it holds and the position rides along in the source, where it stays readable.
        var rows = drafts.Select(draft => new MappingRow(
            from.Key(draft.SourceWordId).Own.ToString(),
            to.Key(draft.TargetWordId).Own.ToString(),
            "renders",
            MappingMethods.Aligner,
            Routes.Written(draft.Translation),
            $"{CompositionPipeline.Machine} {pair.Model}, symmetrised och"
            + (prior.Known ? ", rescored on ETCBC phrase and clause structure" : string.Empty)
            + (double.IsNaN(draft.Position)
                ? $", {NameLists.AddedSource}"
                : string.Create(CultureInfo.InvariantCulture, $", position {draft.Position:F4}"))
            + (note is null ? string.Empty : $"; {note}"),
            null, null, null)).ToList();
        return (rows, new AlignmentOutcome(pair.From, pair.To, answer.Addresses.Count, proposed, collapsed, below, rows.Count, started.Elapsed));
    }

    /// <summary>
    /// What the model proposes for a pair, without writing anything, every answer from <paramref name="floor"/> up.
    ///
    /// Composition needs this rather than the rows already written, and the difference matters: a pair is written only if
    /// it clears the threshold on its own, but a pair that two routes both propose faintly is not faint evidence. Genesis
    /// 1:3 has "стал" against יְהִי at 0.16 by one route and 0.22 by the other — neither is worth writing alone, and the two
    /// together are worth a third of a reader's trust, which is more than the threshold asks.
    /// </summary>
    /// <param name="workspace">The workspace's name under the store's alignments.</param>
    /// <remarks>
    /// A pooled pair whose language keeps a pair below a confidence only where both directions of the model name it
    /// (<see cref="AlignmentPipeline.MeasuredThreshold.AgreedBelow"/>, PRB-1743) proposes under the same rule: a Ge'ez word
    /// whose Greek link its own file drops as unreliable would otherwise still reach the Hebrew through it (PRB-1746).
    /// </remarks>
    public async Task<IReadOnlyList<(long From, long To, double Confidence)>> Proposals(
        string fromSlug,
        string toSlug,
        string workspace,
        double floor,
        bool asWritten = false,
        Selection selection = Selection.BestPerSource,
        string modelType = "ibm4",
        IReadOnlySet<int>? books = null,
        CancellationToken cancellationToken = default)
    {
        var from = texts[fromSlug];
        var to = texts[toSlug];
        var pooled = !asWritten && books is null && AlignmentPipeline.Pool(fromSlug, toSlug) is not null;
        var agreedBelow = pooled ? AlignmentPipeline.Measured.GetValueOrDefault(from.Language)?.AgreedBelow : null;
        var answer = await Answer(fromSlug, toSlug, workspace, modelType, asWritten, books, SameVersification(fromSlug, toSlug), cancellationToken,
            agreed: agreedBelow is not null);
        var agreement = agreedBelow is { } agreedFrom
            ? new AlignmentPipeline.Agreement(AlignmentPipeline.PairsOf(answer.Agreed!, answer.Addresses.Count), agreedFrom)
            : null;
        var (drafts, _, _, _) = AlignmentPipeline.Read(
            answer.File, answer.Addresses, answer.Source, answer.Target, floor, selection, Syntax(toSlug),
            AlignmentPipeline.MarksNoNames(from.Language), AlignmentPipeline.BothMarkTheirNames(from.Language, to.Language), agreement: agreement);
        return [.. drafts.Select(d => (d.SourceWordId, d.TargetWordId, d.Translation))];
    }

    private sealed record Stored(
        string File,
        List<(int, int, int)> Addresses,
        Dictionary<(int, int, int), List<Word>> Source,
        Dictionary<(int, int, int), List<Word>> Target,
        string? Agreed = null);

    /// <summary>
    /// The answer stored for the pair's lines as the words stand. Only the source is read as written: the target's own
    /// reduction is not a hedge — BHSA's consonantal text and Nestle's lemmas are the forms those texts themselves carry.
    /// A trial over a few books trains a model of its own rather than reading the pool's.
    /// </summary>
    /// <param name="agreed">Whether the answer holding only the pairs both directions of the model name is read too: a pool's pair alone has one.</param>
    private async Task<Stored> Answer(
        string fromSlug, string toSlug, string workspace, string modelType, bool asWritten, IReadOnlySet<int>? books, bool lettered,
        CancellationToken cancellationToken, bool agreed = false)
    {
        var pool = asWritten || books is not null ? null : AlignmentPipeline.Pool(fromSlug, toSlug);
        Func<WordForms, string> sourceForm = asWritten ? AlignmentPipeline.Written : word => AlignmentPipeline.Reduce(word);
        Func<WordForms, string> targetForm = pool is null ? word => AlignmentPipeline.Comparable(word) : AlignmentPipeline.Pooled;
        var source = AlignedWords.Read(texts[fromSlug], sourceForm, lettered, books: books, strong: Served(fromSlug),
            hebrew: pool is null ? Hebrew(fromSlug, lettered) : null);
        var target = AlignedWords.Read(texts[toSlug], targetForm, lettered, pool is not null, books, Served(toSlug),
            hebrew: pool is null ? Hebrew(toSlug, lettered) : null);
        var refused = Refused(fromSlug, toSlug);
        source = AlignmentPipeline.Unmet(source, refused);
        target = AlignmentPipeline.Unmet(target, refused);
        var addresses = AlignmentPipeline.Shared(fromSlug, toSlug, source, target);
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" share no verse in the canonical frame: both texts need their words and frames in the store.");
        }

        Directory.CreateDirectory(Path.Combine(Root, workspace));
        if (pool is null)
        {
            if (agreed)
            {
                throw new InvalidOperationException(
                    $"{fromSlug}-{toSlug} is aligned by a model of its own, and only a pool's pairs keep the answer both directions agree on.");
            }

            return new Stored(
                await pipeline.Align(fromSlug, toSlug, Path.Combine(Root, workspace), modelType, addresses, source, target, cancellationToken),
                addresses, source, target);
        }

        var poolWorkspace = Path.Combine(Root, $"{fromSlug}-{string.Join('-', pool)}");
        var partners = PoolPartners(fromSlug, sourceForm);
        var file = await pipeline.AlignPooled(fromSlug, pool, Path.Combine(Root, workspace), poolWorkspace,
            modelType, addresses, source, target, partners, cancellationToken);
        var both = agreed
            ? await pipeline.AlignPooled(fromSlug, pool, Path.Combine(Root, workspace), poolWorkspace,
                modelType, addresses, source, target, partners, cancellationToken, AlignmentPipeline.BothDirections)
            : null;
        return new Stored(file, addresses, source, target, both);
    }

    /// <summary>
    /// A text of the source's pool as the pool's model reads it: the source's words as the source's own pair with that text reads
    /// them (its lettered verses where the two number alike, less the chapters their verse links leave out), and the text's words
    /// at their primary addresses. Whichever pair of the pool asks, the model trains on the same lines.
    /// </summary>
    private Func<string, AlignmentPipeline.PoolPartner> PoolPartners(string fromSlug, Func<WordForms, string> sourceForm)
    {
        var read = new Dictionary<bool, Dictionary<(int, int, int), List<Word>>>();
        return partner =>
        {
            var lettered = AlignerSource.Pairs.Any(pair => pair.From == fromSlug && pair.To == partner && pair.Outside is not null)
                           || SameVersification(fromSlug, partner);
            if (!read.TryGetValue(lettered, out var source))
            {
                read[lettered] = source = AlignedWords.Read(texts[fromSlug], sourceForm, lettered, strong: Served(fromSlug));
            }

            return new AlignmentPipeline.PoolPartner(
                AlignmentPipeline.Unmet(source, Refused(fromSlug, partner)),
                AlignedWords.Read(texts[partner], AlignmentPipeline.Pooled, lettered: true, primaryOnly: true, strong: Served(partner)));
        };
    }

    /// <summary>
    /// The two texts' words in the forms given, with no pool, and the addresses both hold, as <see cref="Answer"/> reads a
    /// pair: what the measuring verbs align (<see cref="AlignerMeasures"/>).
    /// </summary>
    public (List<(int, int, int)> Addresses, Dictionary<(int, int, int), List<Word>> Source, Dictionary<(int, int, int), List<Word>> Target)
        Words(string fromSlug, string toSlug, Func<WordForms, string> sourceForm, Func<WordForms, string> targetForm)
    {
        var lettered = SameVersification(fromSlug, toSlug);
        var refused = Refused(fromSlug, toSlug);
        var source = AlignmentPipeline.Unmet(
            AlignedWords.Read(texts[fromSlug], sourceForm, lettered, strong: Served(fromSlug), hebrew: Hebrew(fromSlug, lettered)), refused);
        var target = AlignmentPipeline.Unmet(
            AlignedWords.Read(texts[toSlug], targetForm, lettered, strong: Served(toSlug), hebrew: Hebrew(toSlug, lettered)), refused);
        return (AlignmentPipeline.Shared(fromSlug, toSlug, source, target), source, target);
    }

    /// <summary>Whether two texts number their verses in one tradition, so that the verses each prints with a letter meet.</summary>
    public bool SameVersification(string one, string other) => Versification(texts.Store, one) == Versification(texts.Store, other);

    private static string? Versification(ProductStore store, string slug)
    {
        using var reader = store.Open(TextField.ProductOf(slug), TextField.Schema);
        return reader.Rows().FirstOrDefault(field => field.Field == "versification")?.Value;
    }

    /// <summary>
    /// Whether a pair's lines read the addresses of the Hebrew: an unpooled pair of texts numbered in two traditions, written
    /// wherever they meet, one of which stands a lettered verse at an address not its own (<see cref="AlignedWords.HebrewLettered"/>).
    /// </summary>
    public static bool ReadsHebrew(ProductStore store, AlignedPair pair) =>
        pair.Outside is null
        && AlignmentPipeline.Pool(pair.From, pair.To) is null
        && store.Exists(TextField.ProductOf(pair.From)) && store.Exists(TextField.ProductOf(pair.To))
        && Versification(store, pair.From) != Versification(store, pair.To)
        && (AlignedWords.PlacesLettered(store, pair.From) || AlignedWords.PlacesLettered(store, pair.To));

    /// <summary>
    /// The addresses a verse of the Hebrew stands at, where a text's lettered verses are not read but its frame stands one at an
    /// address not its own; null where the text has none, its lettered verses are read anyway, or the store holds no Hebrew.
    /// </summary>
    private IReadOnlySet<(int, int, int)>? Hebrew(string slug, bool lettered)
    {
        if (lettered || slug == BhsaTextSource.Slug || !texts.Holds(BhsaTextSource.Slug) || !AlignedWords.PlacesLettered(texts.Store, slug))
        {
            return null;
        }

        return texts.Shared<IReadOnlySet<(int, int, int)>>("hebrew-addresses", () =>
        {
            var bhsa = texts[BhsaTextSource.Slug];
            return bhsa.Verses.SelectMany(bhsa.Addresses).Select(address => (address.Book, address.Chapter, address.Verse)).ToHashSet();
        });
    }

    private bool Served(string slug) => TextWordLoad.StrongServed(texts.Store, slug);

    /// <summary>
    /// The chapters of the first text its verse links to the second leave out, under the name <see cref="TwinPassages"/> joins
    /// them at (<see cref="VerseLinkRule"/> over the two frames): empty for a pair joined wherever the two share an address.
    /// </summary>
    public IReadOnlySet<(int Book, int Chapter)> Refused(string fromSlug, string toSlug)
    {
        var declaration = VerseLinkRule.DeclaredPairs.FirstOrDefault(pair => pair.From == fromSlug && pair.To == toSlug);
        if (declaration is not { AgreeingChaptersOnly: true })
        {
            return new HashSet<(int, int)>();
        }

        IEnumerable<(int, int, int)> Reached(ProductText text) =>
            text.Verses.SelectMany(text.Addresses).Select(address => (address.Book, address.Chapter, address.Verse)).Distinct()
                .Select(TwinPassages.Joined);

        return VerseLinkRule.Differing(Reached(texts[fromSlug]), Reached(texts[toSlug]), declaration.Without);
    }

    /// <summary>
    /// The words of a text standing, by their verse's primary address, where another text has no verse at any address: the
    /// King James's Apocrypha against the Greek it was translated from, and not its Old Testament, which renders the Hebrew.
    /// </summary>
    public static HashSet<long> Beyond(ProductText text, ProductText outside)
    {
        var there = outside.Verses.SelectMany(outside.Addresses).Select(address => (address.Book, address.Chapter, address.Verse)).ToHashSet();
        return [.. text.Verses.Where(verse => !there.Contains(text.Primary(verse))).SelectMany(text.InVerse).Select(id => (long)id)];
    }

    /// <summary>
    /// The target's clause and phrase structure, where it has one: BHSA's, drafted from the ETCBC analysis as the build drafts
    /// <c>word_group</c>, each word by its place in BHSA's words. Every other text yields an empty prior that leaves the
    /// aligner's scores as it found them.
    /// </summary>
    public SyntaxPrior Syntax(string slug) =>
        slug != Analysed ? SyntaxPrior.Of() : Syntaxes.GetValue(texts.Store, _ => new Lazy<SyntaxPrior>(() => ReadSyntax(texts[slug]))).Value;

    /// <summary>The one text whose syntax the corpus holds.</summary>
    public const string Analysed = "BHSA";

    /// <summary>What <see cref="Syntax"/> reads beyond BHSA's words: the analysis and the books' order its slots follow.</summary>
    public static IEnumerable<string> SyntaxInputs(ProductStore store) =>
        [SyntaxSource, .. new[] { TextBook.ProductOf(Analysed) }.Select(store.Find).OfType<string>()];

    private const string SyntaxSource = "etcbc";

    private SyntaxPrior ReadSyntax(ProductText bhsa)
    {
        Dictionary<int, int> positions;
        using (var reader = texts.Store.Open(TextBook.ProductOf(Analysed), TextBook.Schema))
        {
            positions = reader.Rows().ToDictionary(book => book.Book, book => book.Position);
        }

        var bySlot = new Dictionary<int, long>(bhsa.Words.Count);
        var slot = 0;
        foreach (var id in Enumerable.Range(0, bhsa.Words.Count).OrderBy(id => positions[bhsa.Words[id].Key.Verse.Book])
                     .ThenBy(id => bhsa.Words[id].Key.Verse.Chapter).ThenBy(id => bhsa.Words[id].Key.Verse.Verse).ThenBy(id => bhsa.Words[id].Key.Position))
        {
            bySlot[++slot] = id;
        }

        return SyntaxPrior.Of(SyntaxGroups.Drafts(BhsaProject.Load(texts.Store.FullPath(SyntaxSource)), bySlot));
    }

    /// <summary>
    /// The word pairs of the two texts their pair's rulings refuse to the aligner (<c>mapping/&lt;pair&gt;/rulings</c>: a
    /// <c>refuse</c> of one word on each side whose source the aligner's begins with), by their ids in each text: statistical
    /// pairings the held editions' wording contradicts, which no run writes, not even as a claim on a stated link. A refusal
    /// of every source is the merge's to apply, and a word the text lacks names nothing.
    /// </summary>
    public HashSet<(long From, long To)> Rejected(string fromSlug, string toSlug)
    {
        var found = new HashSet<(long, long)>();
        var product = MappingProducts.Of(MappingProducts.Pair(fromSlug, toSlug), MappingProducts.Rulings);
        if (!texts.Store.Exists(product))
        {
            return found;
        }

        using var reader = texts.Store.Open(product, RulingRow.Schema);
        foreach (var ruling in reader.Rows())
        {
            if (ruling.Action != RulingRow.Refuse || ruling.Source.Length == 0
                || !CompositionPipeline.Machine.StartsWith(ruling.Source, StringComparison.Ordinal)
                || MappingText.Split(ruling.From).Count() != 1 || MappingText.Split(ruling.To).Count() != 1
                || Locate(texts[fromSlug], fromSlug, ruling.From) is not { } from
                || Locate(texts[toSlug], toSlug, ruling.To) is not { } to)
            {
                continue;
            }

            found.Add((from, to));
        }

        return found;
    }

    private static long? Locate(ProductText text, string slug, string own)
    {
        var key = WordKey.Parse(own, slug);
        if (!text.Has(key.Verse))
        {
            return null;
        }

        foreach (var id in text.InVerse(key.Verse))
        {
            if (text.Words[id].Key.Position == key.Position)
            {
                return id;
            }
        }

        return null;
    }
}

/// <summary>
/// A pair the aligner is run over, as the corpus's runs of <c>align</c> ran it: the model, the threshold where it is not the
/// one measured for the source's language, and the text whose verses the source was not translated alongside the target.
/// </summary>
/// <param name="Outside">
/// The model still trains on every verse the pair shares, and only the source's words standing where this text has no verse
/// are written: the King James's Apocrypha was translated from the Greek and the rest of its Old Testament from the Hebrew,
/// so against the Septuagint it renders the one and only resembles the other. Lettered verses are read too.
/// </param>
internal sealed record AlignedPair(string From, string To, string Model = "ibm4", double? Minimum = null, string? Outside = null)
{
    public string Name => MappingProducts.Pair(From, To);
}

/// <summary>
/// The <c>aligner</c> file of a pair from SIL's aligner (<see cref="AlignerFiles"/>): the answer stored in
/// <c>Resources/Alignments</c> for the two texts' words as they stand, read into rows. The build never trains: where no
/// stored answer was made from the words the store holds, the pair's file stays as it was and its rows on a moved verse
/// wait for <c>forge align</c> (D1).
/// </summary>
internal sealed class AlignerSource : IPairSource
{
    /// <summary>The pairs aligned, as the corpus's <c>align</c> runs last ran them (the recipe of 2026-10-15).</summary>
    public static readonly IReadOnlyList<AlignedPair> Pairs =
    [
        new("BSB", "KJV"),
        new("UBIO", "KJV"),
        new("ALEX", "NESTLE1904"),
        new("DRA", "VULGCLEM"),
        new("BRENTON", "GRCBRENT"),
        new("ALEX", "GRCBRENT"),
        new("GRCBRENT", "KJV"),
        new("WEB", "GRCBRENT", Outside: "BHSA"),
        new("RUSV", "GRCBRENT", Outside: "BHSA"),
        new("KJV", "GRCBRENT", Outside: "BHSA"),
        new("RUSV", "KJV"),
        new("SIN", "NESTLE1904"),
        new("VAT", "NESTLE1904"),
        new("GEEZ81", "GRCBRENT"),
        new("GEEZ81", "SWETE"),
        new("GEEZ81", "NESTLE1904"),
        new("GEEZ81", "RP2018"),
    ];

    private readonly IReadOnlyList<AlignedPair> _pairs;

    public AlignerSource(IReadOnlyList<AlignedPair>? pairs = null) => _pairs = pairs ?? Pairs;

    /// <summary>Whether a pair whose stored answers were made from other words is trained here (<c>forge align</c>), or waits.</summary>
    public bool Trains { get; init; }

    public string Method => MappingMethods.Aligner;

    public string Name => "aligner";

    public bool States(string pair) => _pairs.Any(p => p.Name == pair);

    public bool Makes(string source) => source.StartsWith(CompositionPipeline.Machine + " ", StringComparison.Ordinal);

    public IEnumerable<string> Inputs(ProductStore store, string pair)
    {
        var aligned = Declared(pair);
        var pool = AlignmentPipeline.Pool(aligned.From, aligned.To);
        var read = new[] { aligned.From, aligned.To }.Concat(pool ?? []).Concat(aligned.Outside is { } outside ? [outside] : []).Distinct().ToList();
        return
        [
            .. read.SelectMany(text => ProductText.Products(text).Append(VerseManifestRow.ProductOf(text))).Select(store.Find).OfType<string>(),
            .. AlignerFiles.StoredAnswers(store, pair),
            .. pool is null ? [] : AlignerFiles.StoredAnswers(store, $"{aligned.From}-{string.Join('-', pool)}").Concat(PoolStamps(store, aligned.From, pool)),
            .. aligned.To == AlignerFiles.Analysed ? AlignerFiles.SyntaxInputs(store) : [],
            .. AlignerFiles.ReadsHebrew(store, aligned) && !read.Contains(BhsaTextSource.Slug)
                ? ProductText.Products(BhsaTextSource.Slug).Select(store.Find).OfType<string>()
                : [],
            .. GeezVerseMap.InputsFor(read),
        ];
    }

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var files = new AlignerFiles(texts, new AlignmentPipeline(null, new LineLogger<AlignmentPipeline>(log)) { Trains = Trains });
        var (rows, outcome) = files.Align(Declared(pair)).GetAwaiter().GetResult();
        log(outcome.ToString());
        return rows;
    }

    private AlignedPair Declared(string pair) =>
        _pairs.FirstOrDefault(p => p.Name == pair) ?? throw new ArgumentException($"{pair} is not a pair the aligner runs over.", nameof(pair));

    /// <summary>The stamps of a pool's trained models, which name the training a pair's answer was aligned with.</summary>
    private static IEnumerable<string> PoolStamps(ProductStore store, string from, IReadOnlyList<string> pool)
    {
        var folder = Path.Combine(store.ResourcesPath, AlignmentStore.FolderName, $"{from}-{string.Join('-', pool)}");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "trained.txt", SearchOption.AllDirectories)
                .Where(file => !file.Contains(".training-", StringComparison.Ordinal))
                .Select(file => Path.GetRelativePath(store.ResourcesPath, file).Replace('\\', '/')).Order(StringComparer.Ordinal)
            : [];
    }
}

/// <summary>A logger that hands each line to a producer's log, as the verbs that write method files report.</summary>
internal sealed class LineLogger<T>(Action<string> log) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            log(formatter(state, exception));
        }
    }
}
