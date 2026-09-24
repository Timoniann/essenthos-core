using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Nestle;
using Essenthos.Core.Tischendorf;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// Tischendorf's editio octava critica maior, 1869-72 — the other edition Nestle voted his text
/// out of, and the Sinaiticus pole of nineteenth-century criticism.
///
/// Tischendorf found Codex Sinaiticus and weighed it accordingly, so where he and Westcott and Hort
/// disagree they disagree systematically rather than at random: Mark 1:1 without <em>Son of God</em>,
/// John 1:18 with <em>only-begotten Son</em> against their <em>only-begotten God</em>, Luke
/// 22:43-44 kept where they double-bracket it, Acts 20:28 <em>the church of the Lord</em> against
/// their <em>of God</em>. That systematic disagreement is what makes the three-way decomposition
/// legible: at each of those places Nestle followed two of the three, and the corpus can now say
/// which two.
///
/// This is the text alone. The eighth edition's apparatus — the reason it is the eighth edition
/// anyone cites — is in neither this release nor any other free one, so a reader is shown which
/// editions read what and never Tischendorf's evidence for it.
/// </summary>
internal static class TischendorfTextSource
{
    public const string Slug = Sources.TischendorfSlug;

    /// <summary>The file stem of each book, and its place in the canon.</summary>
    private static readonly (string File, int Canonical)[] Canon =
    [
        ("MT", 40), ("MR", 41), ("LU", 42), ("JOH", 43), ("AC", 44), ("RO", 45), ("1CO", 46), ("2CO", 47),
        ("GA", 48), ("EPH", 49), ("PHP", 50), ("COL", 51), ("1TH", 52), ("2TH", 53), ("1TI", 54), ("2TI", 55),
        ("TIT", 56), ("PHM", 57), ("HEB", 58), ("JAS", 59), ("1PE", 60), ("2PE", 61), ("1JO", 62), ("2JO", 63),
        ("3JO", 64), ("JUDE", 65), ("RE", 66),
    ];

    /// <summary>The release read, which is also the folder the fetch script writes.</summary>
    public const string Release = "2.8";

    public static IReadOnlyList<string> Books => [.. Canon.Select(book => book.File)];

    /// <summary>Where a book of this edition stands in the shared canon.</summary>
    public static int Canonical(string book) =>
        Canon.FirstOrDefault(entry => entry.File == book) is { Canonical: > 0 } found
            ? found.Canonical
            : throw new InvalidOperationException($"Tischendorf has no book \"{book}\".");

    /// <summary>
    /// The only address the edition writes twice, and the count of verses that has to be. Anything
    /// else doubled is a file this loader has not seen, and loading it would print those verses
    /// with every word in them twice.
    /// </summary>
    private static readonly (int Chapter, int Verse)[] PericopeAdulterae =
    [
        (7, 53), (8, 1), (8, 2), (8, 3), (8, 4), (8, 5), (8, 6), (8, 7), (8, 8), (8, 9), (8, 10), (8, 11),
    ];

    private const string PericopeBook = "JOH";

    /// <summary>
    /// Public domain by the statement inside the data package itself, which is where the grant
    /// actually is: the repository has no LICENSE file and its README states no terms, while
    /// <c>word-per-line/2.8/README.txt</c> and <c>README-short.txt</c> both say <c>This text and its
    /// analysis are in the Public Domain. Copy freely.</c> and the OSIS export's header repeats
    /// <c>Public Domain. Copy freely.</c> Read on 2026-09-06 and kept beside the data.
    ///
    /// The claim covers the analysis as well as the text, made by the people who did the analysis
    /// and recording where each part of the grant came from — G. Clint Yale gave permission to
    /// distribute his accented Tischendorf in the public domain, Robinson's Westcott-Hort was
    /// already there, and Sandborg-Petersen released the edition built out of them. No ShareAlike,
    /// no NonCommercial, no attribution condition — so the three names are recorded here because
    /// this project attributes every source it did not produce, and not because a licence asks.
    /// </summary>
    public static readonly TextDefinition Definition = new(
        Slug: Slug,
        Name: "Tischendorf 8th Edition Greek New Testament",
        NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1872,
        SourceUrl: "https://github.com/morphgnt/tischendorf-data",
        RightsHolder: null,
        Licence: "Public Domain",
        LicenceUrl: "https://github.com/morphgnt/tischendorf-data",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Alexandrian")
    {
        Editors = "Lobegott Friedrich Constantin von Tischendorf (1815-1874)",
        Edition = "Editio octava critica maior: volume I 1869, volume II 1872, the text without its apparatus",
        About = "The eighth of Tischendorf's critical editions and the one everybody means by his "
                + "name. He had found Codex Sinaiticus at Saint Catherine's in 1844 and 1859, and this "
                + "edition is where he weighed it: against the Received Text it drops the doxology of "
                + "the Lord's Prayer, Acts 8:37 and the Johannine comma, and against Westcott and Hort "
                + "it drops Son of God from the first line of Mark, reads only-begotten Son at John "
                + "1:18, keeps the sweat like blood at Luke 22:43, and reads the church of the Lord at "
                + "Acts 20:28. Nestle 1904 is a majority of this edition, Westcott and Hort, and Weiss, "
                + "so it is not a witness against Nestle but one of the votes Nestle counted. The text "
                + "read here is G. Clint Yale's accented transcription, and its morphology, Strong "
                + "numbers and lemmas were ported from Maurice Robinson's Westcott-Hort by Ulrik "
                + "Sandborg-Petersen, which is also why they are not independent testimony about the "
                + "words: where this edition and the Westcott-Hort agree about a parse, one of them "
                + "was copied from the other.",
        RightsNote = "The public-domain grant is in the data package rather than at the top of the "
                     + "repository, which has no licence file at all, and it names three parties: Yale "
                     + "for the accented text, Robinson for the analysis it was ported from, and "
                     + "Sandborg-Petersen as editor. All three are recorded because the licence asks "
                     + "for none of them.",
        Citation = "Tischendorf, C. von (1869-1872). Novum Testamentum Graece, editio octava critica "
                   + "maior; digital edition ed. U. Sandborg-Petersen, release 2.8 (2019).",
    };

    public static TextSource Read(string folder)
    {
        var books = new List<BookDraft>(Canon.Length);
        var position = 0;

        foreach (var (file, canonical) in Canon)
        {
            position++;
            var book = TischendorfReader.Read(File.ReadAllText(
                Path.Combine(folder, "word-per-line", Release, "Unicode", $"{file}.txt")));

            Refuse(file, book);

            var name = BibleBookAbbreviation.GetByOrdinal(canonical)?.FullName.Full
                       ?? throw new InvalidOperationException(
                           $"The Tischendorf book {file} is canonical number {canonical}, which has no name. " +
                           $"Add it to {nameof(BibleBookAbbreviation)} — until then this text cannot be placed " +
                           "beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical,
                Position: position,
                Name: name,
                Slug: Slugs.Of(name),
                Chapters: [.. book.Verses
                    .GroupBy(verse => verse.Chapter)
                    .OrderBy(chapter => chapter.Key)
                    .Select(chapter => new ChapterDraft(
                        chapter.Key,
                        [.. chapter.OrderBy(verse => verse.Number).Select(Verse)]))]));
        }

        return GreekWitnessNumbers.Apply(new TextSource(Definition, books));
    }

    /// <summary>
    /// A doubled address anywhere but the woman taken in adultery, which is the one passage this
    /// edition prints twice. The reader keeps the later of the two printings; that is the right
    /// answer for the pericope and a guess anywhere else, so anywhere else this stops.
    /// </summary>
    private static void Refuse(string file, TischendorfBook book)
    {
        var expected = file == PericopeBook ? PericopeAdulterae : [];
        if (book.Doubled.OrderBy(a => a.Chapter).ThenBy(a => a.Verse).SequenceEqual(expected))
        {
            return;
        }

        var addresses = string.Join(", ", book.Doubled.Select(a => $"{a.Chapter}:{a.Verse}"));
        throw new InvalidOperationException(
            $"The Tischendorf book {file} writes {book.Doubled.Count} addresses twice ({addresses}). Only "
            + "John 7:53-8:11 is printed twice in this edition, once in the Bezan form and once in the "
            + "ordinary one, and the reader keeps the second. Anywhere else that is a file nobody here has "
            + "read, and keeping the second copy of it would be a guess about which of two texts Tischendorf "
            + "printed.");
    }

    private static VerseDraft Verse(TischendorfVerse verse) =>
        new(verse.Number, [.. verse.Words.Select((word, at) => Draft(word, at == verse.Words.Count - 1))]);

    /// <summary>
    /// The word as printed, not as corrected. The Kethiv is what stands in Tischendorf and the
    /// Qere is this digital edition's editor saying what he thinks it should have been — eleven
    /// words in the New Testament, and the corpus holds editions rather than improvements of them.
    /// The parse and the number follow the Qere, which is the editor's business and is recorded
    /// beside the word where the two differ.
    /// </summary>
    private static WordDraft Draft(TischendorfWord word, bool last)
    {
        var (surface, trailer) = NestleParser.ParseWordAndTrailer(word.Kethiv);

        return new WordDraft(
            Surface: surface,
            Trailer: last ? trailer.TrimEnd() : trailer,
            Lemma: word.Lemma,
            StrongNumber: $"G{word.Strong}",
            Morphology: Morphology(word));
    }

    private static string Morphology(TischendorfWord word)
    {
        var features = new Dictionary<string, string>(5) { ["robinson"] = word.Morphology };

        if (word.Homonym is { } homonym)
        {
            features["homonym"] = homonym;
        }

        // Only where the two lexica spell the headword differently, which is what the second lemma
        // is for. Strong writes Δαβίδ where the analytical lexicon writes Δαυίδ, and a reader
        // looking up the number needs the spelling the dictionary they are holding uses.
        if (word.StrongLemma != word.Lemma)
        {
            features["strongLemma"] = word.StrongLemma;
        }

        if (word.Qere != word.Kethiv)
        {
            features["qere"] = word.Qere;
        }

        if (word.Break is { } division)
        {
            features["break"] = division;
        }

        return JsonSerializer.Serialize(features);
    }
}
