using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The two printed Greek editions Robinson's composite holds, read out of one parse.
///
/// This is the text the King James New Testament was translated from, and it is the reason 4,057 of
/// its tagged words currently point at nothing: the corpus has only Nestle 1904 to offer them, and
/// where the editions differ there is no Greek word to reach. John 1:18 reads "only begotten Son"
/// against Nestle's "only begotten God", and until now the platform said nothing at all about why.
///
/// Stephanus 1550 comes free with the same file — it is the first alternative of every group where
/// Scrivener is the second — and it is a fair test of the model: two Greek witnesses in one corpus
/// is the thing the old schema could not hold at any price.
/// </summary>
internal static class TextusReceptusTextSource
{
    /// <summary>The file stem of each book, and its place in the canon.</summary>
    private static readonly (string File, int Canonical)[] Canon =
    [
        ("MT", 40), ("MR", 41), ("LU", 42), ("JOH", 43), ("AC", 44), ("RO", 45), ("1CO", 46), ("2CO", 47),
        ("GA", 48), ("EPH", 49), ("PHP", 50), ("COL", 51), ("1TH", 52), ("2TH", 53), ("1TI", 54), ("2TI", 55),
        ("TIT", 56), ("PHM", 57), ("HEB", 58), ("JAS", 59), ("1PE", 60), ("2PE", 61), ("1JO", 62), ("2JO", 63),
        ("3JO", 64), ("JUDE", 65), ("RE", 66),
    ];

    public static IReadOnlyList<string> Books => [.. Canon.Select(book => book.File)];

    /// <summary>Where a book of this edition stands in the shared canon.</summary>
    public static int Canonical(string book) =>
        Canon.FirstOrDefault(entry => entry.File == book) is { Canonical: > 0 } found
            ? found.Canonical
            : throw new InvalidOperationException($"The Textus Receptus has no book \"{book}\".");

    public static string Slug(Edition edition) =>
        edition == Edition.Scrivener1894 ? "scrivener1894" : "stephanus1550";

    /// <summary>
    /// Both editions are long out of copyright, and Robinson's parsing and Strong numbers are
    /// released into the public domain by the repository that holds them — its README says
    /// <c>License? Public Domain. Copy freely.</c> and nothing else, read on 2026-08-31 and kept
    /// beside the data, because a licence that lives only at a URL is one nobody can check offline.
    ///
    /// The re-wrappings are more restrictive than the original: the CrossWire SWORD module and the
    /// Zefania build both carry CC BY-NC-SA over data that is public domain at source. Take the
    /// original.
    /// </summary>
    private static TextDefinition Definition(Edition edition) => edition == Edition.Scrivener1894
        ? new TextDefinition(
            Slug: Slug(edition),
            Name: "Scrivener 1894 Textus Receptus",
            NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
            Kind: TextKind.PrintedEdition,
            Language: "grc",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.English,
            PublishedYear: 1894,
            SourceUrl: "https://github.com/byztxt/greektext-textus-receptus",
            RightsHolder: null,
            Licence: "Public Domain",
            LicenceUrl: "https://github.com/byztxt/greektext-textus-receptus",
            Redistribution: Redistribution.PublicDomain,
            TextualFamily: "Byzantine")
        : new TextDefinition(
            Slug: Slug(edition),
            Name: "Stephanus 1550 Textus Receptus",
            NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
            Kind: TextKind.PrintedEdition,
            Language: "grc",
            Direction: TextDirection.LeftToRight,
            Versification: Versification.English,
            PublishedYear: 1550,
            SourceUrl: "https://github.com/byztxt/greektext-textus-receptus",
            RightsHolder: null,
            Licence: "Public Domain",
            LicenceUrl: "https://github.com/byztxt/greektext-textus-receptus",
            Redistribution: Redistribution.PublicDomain,
            TextualFamily: "Byzantine");

    public static TextSource Read(string folder, Edition edition)
    {
        var books = new List<BookDraft>(Canon.Length);
        var position = 0;

        foreach (var (file, canonical) in Canon)
        {
            position++;
            var verses = UtrReader.Read(
                File.ReadAllText(Path.Combine(folder, "parsed", $"{file}.UTR")), edition);

            var name = BibleBookAbbreviation.GetByOrdinal(canonical)?.FullName.Full
                       ?? throw new InvalidOperationException(
                           $"The Textus Receptus book {file} is canonical number {canonical}, which has no name. " +
                           $"Add it to {nameof(BibleBookAbbreviation)} — until then this text cannot be placed " +
                           "beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical,
                Position: position,
                Name: name,
                Slug: Slugs.Of(name),
                Chapters: [.. verses
                    .GroupBy(v => v.Chapter)
                    .OrderBy(chapter => chapter.Key)
                    .Select(chapter => new ChapterDraft(
                        chapter.Key,
                        [.. chapter.OrderBy(v => v.Number).Select(Verse)]))]));
        }

        return new TextSource(Definition(edition), books);
    }

    private static VerseDraft Verse(UtrVerse verse) =>
        new(verse.Number, [.. verse.Words.Select((word, at) => new WordDraft(
            Surface: BetaCode.ToGreek(word.Surface),
            Trailer: at == verse.Words.Count - 1 ? string.Empty : " ",
            StrongNumber: word.Strong is null ? null : $"G{word.Strong}",
            Morphology: Morphology(word)))]);

    /// <summary>
    /// Robinson's parse code, and the inflection code a verb carries beside its Strong number. Both
    /// are kept as they were written rather than expanded: nothing here reads them yet, and a code
    /// expanded by a guess is worse than one left alone.
    /// </summary>
    private static string? Morphology(UtrWord word)
    {
        if (word.Morphology is null && word.Inflection is null)
        {
            return null;
        }

        var features = new Dictionary<string, string>(2);
        if (word.Morphology is { } parse)
        {
            features["robinson"] = parse;
        }

        if (word.Inflection is { } inflection)
        {
            features["inflection"] = inflection;
        }

        return JsonSerializer.Serialize(features);
    }
}
