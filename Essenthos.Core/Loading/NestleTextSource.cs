using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Nestle;
using Essenthos.Core.Strong;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Nestle 1904 Greek New Testament. Its words arrive as one flat list in document order, each
/// carrying its own address, so the structure is recovered from the addresses rather than read.
/// </summary>
internal static class NestleTextSource
{
    public const string Slug = "nestle1904";

    /// <summary>
    /// The 1904 edition is out of copyright; the transcription, morphology and Strong numbers are
    /// released into the public domain by biblicalhumanities.org, which is what its morphology
    /// readme states — read on 2026-08-31. So there is no rights holder to name, and serving it is
    /// unconditioned.
    /// </summary>
    public static readonly TextDefinition Definition = new(
        Slug: Slug,
        Name: "Nestle 1904 Greek New Testament",
        NameNative: "Η ΚΑΙΝΗ ΔΙΑΘΗΚΗ",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.English,
        PublishedYear: 1904,
        SourceUrl: "https://github.com/biblicalhumanities/Nestle1904",
        RightsHolder: null,
        Licence: "CC0-1.0",
        LicenceUrl: "https://creativecommons.org/publicdomain/zero/1.0/",
        Redistribution: Redistribution.PublicDomain,
        TextualFamily: "Alexandrian")
    {
        Editors = "Eberhard Nestle",
        Edition = "The 1904 British and Foreign Bible Society printing",
        About = "Nestle collated no manuscripts for this text: he built it by combining the printed "
                + "editions of Tischendorf, Westcott and Hort, and Weymouth, which is why it stands "
                + "close to the modern critical text without being one. He published the first "
                + "edition in 1898; the British and Foreign Bible Society printed the 1904 edition "
                + "read here, and the Nestle name has been on their Greek New Testament ever since. "
                + "The digital edition was transcribed by Diego Renato dos Santos, given its "
                + "morphology by Ulrik Sandborg-Petersen and marked up by Jonathan Robie.",
    };

    public static TextSource Read(string nestlePath, string? glossPath = null)
    {
        var glosses = glossPath is null ? null : File.ReadAllText(glossPath);
        return Build(new NestleParser().Parse(File.ReadAllText(nestlePath), glosses));
    }

    public static TextSource Build(IReadOnlyList<NestleWord> words)
    {
        var books = new List<BookDraft>(27);
        var position = 0;

        foreach (var byBook in words.GroupBy(w => w.Book))
        {
            position++;
            var canonical = BibleBookAbbreviation.GetAbbreviation(byBook.Key)
                            ?? throw new InvalidOperationException(
                                $"Nestle addresses a book \"{byBook.Key}\" that has no canonical ordinal. Add it " +
                                $"to {nameof(BibleBookAbbreviation)} — until then this text cannot be placed " +
                                "beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical.Ordinal,
                Position: position,
                Name: canonical.FullName.Full,
                Slug: Slugs.Of(canonical.StandardAbbreviation.Full),
                Chapters: byBook
                    .GroupBy(w => w.Chapter)
                    .Select(byChapter => new ChapterDraft(
                        byChapter.Key,
                        byChapter
                            .GroupBy(w => w.Verse)
                            .Select(byVerse => new VerseDraft(byVerse.Key, byVerse.Select(Draft).ToList()))
                            .ToList()))
                    .ToList(),
                Abbreviation: canonical.StandardAbbreviation.Full));
        }

        return new TextSource(Definition, books);
    }

    private static WordDraft Draft(NestleWord word) => new(
        Surface: word.Word,
        Trailer: word.Trailer,
        Lemma: word.Lemma,
        StrongNumber: StrongNumbers.Normalize($"G{word.Strong}"),
        Gloss: string.IsNullOrEmpty(word.Gloss) ? null : word.Gloss,
        Morphology: Morphology(word));

    private static string Morphology(NestleWord word)
    {
        var features = new Dictionary<string, string>(10)
        {
            ["pos"] = word.Pos,
            ["form"] = word.Form,
            ["func"] = word.Func,
            ["normalized"] = word.Normalized,
        };

        Add(features, "case", word.Case);
        Add(features, "number", word.Number);
        Add(features, "gender", word.Gender);
        Add(features, "mood", word.Mood);
        Add(features, "tense", word.Tense);
        Add(features, "voice", word.Voice);
        Add(features, "person", word.Person);

        return JsonSerializer.Serialize(features, MorphologyJson);
    }

    private static readonly JsonSerializerOptions MorphologyJson = new() { WriteIndented = false };

    private static void Add(Dictionary<string, string> features, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            features[name] = value;
        }
    }
}
