using System.Globalization;
using System.Text;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Records;

namespace Essenthos.Core.Loading.CrossReferences;

/// <param name="Set">The set, as <see cref="CrossReferenceSets"/> names it.</param>
/// <param name="Written">False where the set's file was current and nothing was read.</param>
/// <param name="Rows">References written; for the parallels, pairs of verses, each both ways round.</param>
/// <param name="Unread">Pieces of the source that did not read as a verse, and were dropped.</param>
/// <param name="Passages">For the parallels, the passages the pairs belong to.</param>
/// <param name="LeftOut">References read whole that name a verse the English numbering does not have, and were not written.</param>
internal sealed record CrossReferenceOutcome(string Set, bool Written, int Rows, int Unread, int Passages, int LeftOut = 0)
{
    public override string ToString() =>
        !Written
            ? $"The {Set} set of cross references is current"
            : Set == CrossReferenceSets.Parallels
                ? $"{Passages} parallel passages as {Rows} rows of verse pairs"
                : $"{Rows} {Set} cross references, with {Unread} pieces that did not read as a verse and {LeftOut} naming " +
                  "a verse no text has left out";
}

/// <summary>
/// The sets of cross references a reader can choose between beside a verse, as the files the build loads into
/// <c>cross_reference</c>: OpenBible.info's (<c>external/openbible/cross-references</c>), ranked by its readers' votes; the
/// Treasury of Scripture Knowledge's (<c>external/tsk/cross-references</c>), in its own order; and the parallel passages
/// this project finds in the originals (<c>records/derived/parallels</c>), each with the words the two share.
///
/// <para>
/// Each file is made again only when what it is read from moved: OpenBible's file, the Treasury's module, or the Hebrew's
/// and the Greek's words and frames, whose lemmas the parallels are found in.
/// </para>
///
/// <para>
/// A reference whose verse, or the last verse of its run, the English numbering does not have is left
/// out and named in the log: it would send the reader to a verse no text prints.
/// </para>
/// </summary>
internal static class CrossReferenceFiles
{
    public const string Producer = "forge cross-references";

    public const string OpenBibleProduct = "external/openbible/cross-references";

    public const string TreasuryProduct = "external/tsk/cross-references";

    public const string ParallelsProduct = "records/derived/parallels";

    /// <summary>The originals the parallels are found in, each with how a passage is told in it.</summary>
    private static readonly (string Slug, ParallelSettings Settings)[] Originals =
    [
        (BhsaTextSource.Slug, ParallelSettings.Hebrew),
        (Sources.NestleSlug, ParallelSettings.Greek),
    ];

    /// <summary>Writes each set's file whose inputs moved, or every one when forced.</summary>
    public static List<CrossReferenceOutcome> Write(ProductStore store, bool force, ILogger logger) =>
        [OpenBible(store, force, logger), Treasury(store, force, logger), Parallels(store, force, logger)];

    internal static CrossReferenceOutcome OpenBible(ProductStore store, bool force, ILogger logger) =>
        WriteSet(store, CrossReferenceSets.OpenBible, OpenBibleProduct,
            [$"{OpenBibleCrossReferences.Folder}/{OpenBibleCrossReferences.FileName}"], force, logger,
            () => OpenBibleCrossReferences.Read(store.FullPath($"{OpenBibleCrossReferences.Folder}/{OpenBibleCrossReferences.FileName}")));

    internal static CrossReferenceOutcome Treasury(ProductStore store, bool force, ILogger logger) =>
        WriteSet(store, CrossReferenceSets.Treasury, TreasuryProduct,
            [TreasuryReferences.Folder, .. store.Find(TreasuryReferences.CorrectionsProduct) is { } corrections ? [corrections] : Array.Empty<string>()],
            force, logger,
            () => TreasuryReferences.Read(store.FullPath(TreasuryReferences.Folder), TreasuryReferences.Corrections(store)));

    /// <summary>The parallel passages as they would be written, one line each, for reading before they are; writes nothing.</summary>
    public static List<string> MeasureParallels(ProductStore store, ILogger logger)
    {
        var (rows, _) = Detect(store, logger);
        return
        [
            .. rows.Where(row => row.Passage % 2 == 1)
                .GroupBy(row => row.Passage)
                .Select(passage =>
                {
                    var from = passage.Select(row => row.From).ToList();
                    var to = passage.Select(row => row.To).ToList();
                    return $"{passage.Sum(Shared),4} {from.Min()}-{from.Max()} = {to.Min()}-{to.Max()} ({passage.First().MatchedIn})";
                }),
        ];
    }

    private static CrossReferenceOutcome WriteSet(
        ProductStore store, string set, string product, string[] sources, bool force, ILogger logger,
        Func<(List<CrossReferenceRow> Rows, int Unread)> read)
    {
        var file = RecordProducers.File(product);
        var inputs = store.Inputs(sources);
        if (!force && !store.NeedsBuild(file.Product, file.Kind, inputs))
        {
            return new CrossReferenceOutcome(set, false, 0, 0, 0);
        }

        var (all, unread) = read();
        var rows = InTheFrame(set, all, logger);
        Write(store, file, set, rows, inputs);
        return new CrossReferenceOutcome(set, true, rows.Count, unread, 0, all.Count - rows.Count);
    }

    internal static CrossReferenceOutcome Parallels(ProductStore store, bool force, ILogger logger)
    {
        var file = RecordProducers.File(ParallelsProduct);
        var inputs = store.Inputs(Originals.SelectMany(o => new[] { TextWord.ProductOf(o.Slug), FrameFile.Product(o.Slug) })
            .Select(p => store.Find(p) ?? throw new FileNotFoundException(
                $"The parallels are found in the originals' lemmas, and the store holds no {p}. Run forge build, which converts the texts first.")));
        if (!force && !store.NeedsBuild(file.Product, file.Kind, inputs))
        {
            return new CrossReferenceOutcome(CrossReferenceSets.Parallels, false, 0, 0, 0);
        }

        var (rows, passages) = Detect(store, logger);
        Write(store, file, CrossReferenceSets.Parallels, rows, inputs);
        return new CrossReferenceOutcome(CrossReferenceSets.Parallels, true, rows.Count, 0, passages);
    }

    private static void Write(ProductStore store, RecordFile file, string set, List<CrossReferenceRow> rows, IReadOnlyList<ProductInput> inputs)
    {
        var source = CrossReferenceSets.Find(set)!.Source;
        RecordProducers.Write(store, file, rows.Select(row => new string?[]
        {
            Key(row.From), Key(row.To), row.End is { } end ? Key(end) : null, Integer(row.Rank), Integer(row.Votes), row.Note,
            Integer(row.Passage), row.MatchedIn, row.Matched, source,
        }), Producer, inputs);
    }

    /// <summary>
    /// The references whose verses the English numbering has, each verse's ranked again in the order they stood; every
    /// one left out is named.
    /// </summary>
    private static List<CrossReferenceRow> InTheFrame(string set, List<CrossReferenceRow> rows, ILogger logger)
    {
        foreach (var row in rows.Where(row => !EnglishVerses.Has(row)))
        {
            logger.LogWarning("Left out of the {Set} cross references: {From} to {To}{End}, a verse no text has",
                set, row.From, row.To, row.End is { } end ? $" through {end}" : string.Empty);
        }

        return
        [
            .. rows.Where(EnglishVerses.Has)
                .GroupBy(row => row.From)
                .SelectMany(verse => verse.OrderBy(row => row.Rank).Select((row, rank) => row with { Rank = rank + 1 })),
        ];
    }

    /// <summary>
    /// The parallel passages of the Hebrew Old Testament and of the Greek New, each pair of verses
    /// written both ways round so the reader finds it from either side.
    /// </summary>
    private static (List<CrossReferenceRow> Rows, int Passages) Detect(ProductStore store, ILogger logger)
    {
        var passages = new List<(ParallelPassage Passage, string Text)>();
        foreach (var (slug, settings) in Originals)
        {
            var tokens = Tokens(store, slug);
            if (tokens.Count == 0)
            {
                throw new InvalidOperationException($"The parallels are found in {slug}'s lemmas, and its words product holds no word.");
            }

            var found = ParallelDetector.Detect(tokens, settings);
            logger.LogInformation("{Passages} parallel passages in {Text}", found.Count, slug);
            passages.AddRange(found.Select(passage => (passage, slug)));
        }

        var rows = new Dictionary<(VerseAddress From, VerseAddress To), CrossReferenceRow>();
        for (var p = 0; p < passages.Count; p++)
        {
            var (passage, text) = passages[p];
            foreach (var pair in passage.Verses)
            {
                Keep(rows, new CrossReferenceRow(pair.A, pair.B, null, 0, Passage: (2 * p) + 1,
                    MatchedIn: text, Matched: Words(pair.Words.Select(w => (w.A, w.B)))));
                Keep(rows, new CrossReferenceRow(pair.B, pair.A, null, 0, Passage: (2 * p) + 2,
                    MatchedIn: text, Matched: Words(pair.Words.Select(w => (w.B, w.A)))));
            }
        }

        var ranked = rows.Values
            .GroupBy(row => row.From)
            .SelectMany(verse => verse
                .OrderByDescending(Shared)
                .ThenBy(row => row.To)
                .Select((row, rank) => row with { Rank = rank + 1 }))
            .ToList();
        return (ranked, passages.Count);
    }

    /// <summary>A pair found by two passages is kept once, with the more words.</summary>
    private static void Keep(Dictionary<(VerseAddress, VerseAddress), CrossReferenceRow> rows, CrossReferenceRow row)
    {
        if (!rows.TryGetValue((row.From, row.To), out var kept) || Shared(kept) < Shared(row))
        {
            rows[(row.From, row.To)] = row;
        }
    }

    private static int Shared(CrossReferenceRow row) =>
        row.Matched is null ? 0 : row.Matched.Count(c => c == ' ') + 1;

    internal static string Words(IEnumerable<(int From, int To)> words)
    {
        var written = new StringBuilder();
        foreach (var (from, to) in words)
        {
            if (written.Length > 0)
            {
                written.Append(' ');
            }

            written.Append(from).Append('-').Append(to);
        }

        return written.ToString();
    }

    /// <summary>
    /// The words of one original in the order they are read, each at its verse's primary place in the frame, with its
    /// dictionary form, or its letters where it states none; an elided word is not read.
    /// </summary>
    internal static List<LemmaToken> Tokens(ProductStore store, string slug)
    {
        var placed = new Dictionary<VerseKey, VerseKey>();
        using (var frame = store.Open(FrameFile.Product(slug), FrameRow.Schema))
        {
            foreach (var row in frame.Rows())
            {
                foreach (var (own, canonical) in row.Verses())
                {
                    placed[own] = canonical[0];
                }
            }
        }

        var tokens = new List<LemmaToken>(450_000);
        foreach (var (key, lemma) in Lemmas(store, slug))
        {
            var at = placed.TryGetValue(key.Verse, out var address) ? address : FrameFile.AtOwn(key.Verse);
            tokens.Add(new LemmaToken(new VerseAddress(at.Book, at.Chapter, at.Verse), key.Position, lemma));
        }

        // Stable: two verses of the edition at one place keep the edition's order.
        return [.. tokens.OrderBy(t => t.Verse).ThenBy(t => t.Position)];
    }

    /// <summary>Each word that is read, by key, with its lemma or else its letters, in the words product's order.</summary>
    private static IEnumerable<(WordKey Key, string Lemma)> Lemmas(ProductStore store, string slug)
    {
        var relative = store.Find(TextWord.ProductOf(slug)) ?? throw new FileNotFoundException($"The store holds no words of {slug}.");
        if (relative.EndsWith(EssxLayout.Extension, StringComparison.Ordinal))
        {
            using var file = EssxReader.Open(store.FullPath(relative));
            var table = file.Table(EssxFormat.TableName);
            for (var g = 0; g < table.RowGroups; g++)
            {
                var group = table.ReadGroup(g, ["own", "surface", "lemma", "elided"]);
                var (own, surface, lemma, elided) = (group.Column("own"), group.Column("surface"), group.Column("lemma"), group.Column("elided"));
                for (var r = 0; r < group.Rows; r++)
                {
                    if (elided.Boolean(r) != true)
                    {
                        yield return (WordKey.Parse(own.Text(r)!, slug), lemma.Text(r) ?? surface.Text(r)!);
                    }
                }
            }

            yield break;
        }

        using var reader = store.Open(TextWord.ProductOf(slug), TextWord.Schema(slug));
        foreach (var word in reader.Rows().Where(w => !w.Elided))
        {
            yield return (word.Key, word.Lemma ?? word.Surface);
        }
    }

    private static string Key(VerseAddress verse) => new VerseKey(verse.Book, verse.Chapter, verse.Verse).ToString();

    private static string? Integer(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
