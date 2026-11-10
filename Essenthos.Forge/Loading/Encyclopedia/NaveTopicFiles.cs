using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.CrossReferences;
using Essenthos.Core.Products;
using Essenthos.Core.Records;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">False where both files were current and nothing was read.</param>
/// <param name="Topics">Subjects written, which is those citing at least one verse.</param>
/// <param name="References">Runs of verses written, one per chapter a citation reaches.</param>
/// <param name="CrossReferencesOnly">Subjects that only send the reader elsewhere — <em>See WIND</em> — and are not written.</param>
/// <param name="Unread">Pieces of citation that did not read as a book, chapter and verse, and were dropped.</param>
/// <param name="LeftOut">Citations of a chapter or verse the English numbering does not have, which were not written.</param>
internal sealed record TopicOutcome(bool Written, int Topics, int References, int CrossReferencesOnly, int Unread, int LeftOut)
{
    public override string ToString() =>
        !Written
            ? $"{NaveTopicFiles.TopicsProduct} and {NaveTopicFiles.ReferencesProduct} are current"
            : $"{Topics} of Nave's topics with {References} runs of verses, {CrossReferencesOnly} subjects " +
              $"that only point elsewhere left out, {Unread} pieces of citation that did not read and {LeftOut} citing " +
              "a verse no text has left out";
}

/// <summary>
/// Nave's Topical Bible: some five thousand subjects and the verses Orville Nave filed under each, as the files
/// <c>external/bibledata/topics</c> and <c>external/bibledata/topic-references</c> the build loads into <c>topic</c> and
/// <c>topic_reference</c>.
///
/// <para>
/// **On trial.** It is loaded so a reader can see the topics a chapter's verses are filed under
/// beside the chapter, and whether it stays is the owner's decision after seeing it. Nothing else in
/// the corpus reads these tables, so taking it out is dropping them.
/// </para>
///
/// <para>
/// **Read from BibleData's transcription**, the one copy of Nave on this machine. Nave's own text of
/// 1896 is out of copyright; Stephenson's transcription of it is his, under the CC BY 4.0 the rest of
/// that release carries, and is credited as such. Its references are in the King James numbering,
/// which is the shared frame's, so they are stored as they are written.
/// </para>
///
/// <para>
/// Made again only when the index moved. A citation of a chapter or verse the English numbering does not have
/// (MIRACLES' Exodus 3:29) is left out and named in the log. A topic's references keep the order Nave cites them in.
/// </para>
/// </summary>
internal static class NaveTopicFiles
{
    public const string Folder = "BibleData2026";

    public const string FileName = "NavesTopicalDictionary.csv";

    public const string Producer = "forge topics";

    public const string TopicsProduct = "external/bibledata/topics";

    public const string ReferencesProduct = "external/bibledata/topic-references";

    /// <summary>One subject as it is written, with the verses filed under it in the order they are cited.</summary>
    internal sealed record NaveTopic(string Slug, string Name, IReadOnlyList<(string? Heading, NaveEntries.Citation Citation)> Cited);

    /// <summary>Writes both files when the index moved, or when forced.</summary>
    public static TopicOutcome Write(ProductStore store, bool force, ILogger logger)
    {
        var source = $"{Folder}/{FileName}";
        var path = store.FullPath(source);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Nave's Topical Bible is read from {path}, which is not there. It comes with the BibleData " +
                "release; run scripts/fetch-bibledata.ps1, or point Dataset:ResourcesPath at a corpus that has it.",
                path);
        }

        var inputs = store.Inputs(source);
        var topicsFile = RecordProducers.File(TopicsProduct);
        var referencesFile = RecordProducers.File(ReferencesProduct);
        if (!force && !store.NeedsBuild(topicsFile.Product, topicsFile.Kind, inputs)
                   && !store.NeedsBuild(referencesFile.Product, referencesFile.Kind, inputs))
        {
            return new TopicOutcome(false, 0, 0, 0, 0, 0);
        }

        var (topics, outcome) = Read(path, logger);
        RecordProducers.Write(store, topicsFile, topics.Select(t => new string?[] { t.Slug, t.Name, Sources.Naves }), Producer, inputs);
        RecordProducers.Write(store, referencesFile,
            topics.SelectMany(t => t.Cited.Select((cited, index) => new string?[]
            {
                t.Slug, Integer(index + 1), Integer(cited.Citation.Book), Integer(cited.Citation.Chapter),
                Integer(cited.Citation.FirstVerse), Integer(cited.Citation.LastVerse), cited.Heading,
            })),
            Producer, inputs);
        return outcome;
    }

    /// <summary>
    /// Every subject citing a verse of the English numbering, each citation once in the order it is first cited, its
    /// slug its letters with a number after a second subject folding to the same ones.
    /// </summary>
    internal static (List<NaveTopic> Topics, TopicOutcome Outcome) Read(string path, ILogger logger)
    {
        var slugs = new HashSet<string>(StringComparer.Ordinal);
        var topics = new List<NaveTopic>();
        var crossReferencesOnly = 0;
        var unread = 0;
        var leftOut = 0;
        foreach (var row in Csv.Read(path))
        {
            var name = row["subject"].Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var cited = new List<(string? Heading, NaveEntries.Citation Citation)>();
            foreach (var line in NaveEntries.Lines(NaveCorrections.Apply(name, row["entry"])))
            {
                unread += line.Unread;
                foreach (var citation in line.Citations)
                {
                    if (InTheFrame(citation))
                    {
                        cited.Add((line.Heading, citation));
                        continue;
                    }

                    leftOut++;
                    logger.LogWarning("Left out of Nave's {Subject}: {Book} {Chapter}:{First}-{Last}, a verse no text has",
                        name, citation.Book, citation.Chapter, citation.FirstVerse, citation.LastVerse);
                }
            }

            if (cited.Count == 0)
            {
                crossReferencesOnly++;
                continue;
            }

            topics.Add(new NaveTopic(UniqueSlug(name, slugs), name, [.. cited.Distinct()]));
        }

        return (topics, new TopicOutcome(true, topics.Count, topics.Sum(t => t.Cited.Count), crossReferencesOnly, unread, leftOut));
    }

    /// <summary>Whether the chapter, and each verse the citation names, is one the English numbering has.</summary>
    private static bool InTheFrame(NaveEntries.Citation citation) =>
        EnglishVerses.HasChapter(citation.Book, citation.Chapter)
        && (citation.FirstVerse is not { } first || EnglishVerses.Has(new VerseAddress(citation.Book, citation.Chapter, first)))
        && (citation.LastVerse is not { } last || EnglishVerses.Has(new VerseAddress(citation.Book, citation.Chapter, last)));

    /// <summary><c>AARON</c> is <c>aaron</c>; a second subject folding to the same letters takes a number.</summary>
    private static string UniqueSlug(string name, HashSet<string> taken)
    {
        var slug = Slugs.Of(name);
        var unique = slug;
        for (var n = 2; !taken.Add(unique); n++)
        {
            unique = $"{slug}-{n}";
        }

        return unique;
    }

    private static string? Integer(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
