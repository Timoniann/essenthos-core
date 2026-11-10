using Essenthos.Core.Conversion;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Microsoft.Extensions.Logging.Abstractions;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// A few verses of texts written as the store's products — the declared fields, the words, the verses in print order and
/// the frame — for the producers that read files and no database: the aligner's lines, a composition.
/// </summary>
internal static class ProductCorpus
{
    /// <param name="verses">Each verse by its own key (<c>1.1:2a</c>) and its words, in the order the edition prints them.</param>
    /// <param name="frame">Where a verse stands otherwise than at its own address: its own key, then its addresses, the primary first.</param>
    public static void Text(ProductStore store, string slug, string language, (string Verse, string[] Words)[] verses,
        params (string Own, string[] Canonical)[] frame)
    {
        using (var declared = store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", "translation"), new TextField("language", language),
                new TextField("versification", "kjv")]);
        }

        var printed = verses.Select(verse => (Key: VerseKey.Parse(verse.Verse), verse.Words)).ToList();
        using (var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(printed.OrderBy(verse => verse.Key).SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, verse.Key, n + 1), null, word, " ", null, null, null, null, null, null, false, null, null, null))));
        }

        using (var manifest = store.Create(VerseManifestRow.ProductOf(slug), ProductKind.Machine, VerseManifestRow.Schema, "test", []))
        {
            manifest.WriteAll(printed.Select((verse, at) => new VerseManifestRow(verse.Key,
                printed.Take(at).Count(before => (before.Key.Book, before.Key.Chapter) == (verse.Key.Book, verse.Key.Chapter)) + 1,
                verse.Words.Length, new VerseHashes("", ""))));
        }

        using var frames = store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frames.WriteAll(frame.Select(row => new FrameRow(VerseKey.Parse(row.Own), null, [.. row.Canonical.Select(key => VerseKey.Parse(key))], "test")));
    }

    /// <summary>The verses of a chapter of book 1, numbered from 1.</summary>
    public static (string Verse, string[] Words)[] Genesis(params string[][] verses) =>
        [.. verses.Select((words, at) => ($"1.1:{at + 1}", words))];

    /// <summary>The aligner over the store's files, its workspaces under the store's <c>Alignments</c>.</summary>
    public static AlignerFiles Aligner(ProductStore store) =>
        new(new ProductTexts(store), new AlignmentPipeline(null, NullLogger<AlignmentPipeline>.Instance) { Trains = false });

    /// <summary>A workspace as a finished run leaves it: the lines it was trained on, and its answer.</summary>
    public static void Answer(string folder, string[] source, string[] target, params string[] answers)
    {
        Directory.CreateDirectory(Path.Combine(folder, "alignment"));
        File.WriteAllLines(Path.Combine(folder, "source.txt"), source);
        File.WriteAllLines(Path.Combine(folder, "target.txt"), target);
        File.WriteAllLines(Path.Combine(folder, "alignment", "pharaoh.txt"), answers);
    }

    /// <summary>English words as the aligner writes them, one line per verse.</summary>
    public static string[] Stems(params string[] lines) =>
        [.. lines.Select(line => string.Join(' ', line.Split(' ').Select(word => AlignmentTokens.One(EnglishStemmer.Stem(word)))))];
}
