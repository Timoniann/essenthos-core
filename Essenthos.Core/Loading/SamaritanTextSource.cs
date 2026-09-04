using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Samaritan;
using Essenthos.Core.Utils;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Samaritan Pentateuch as a text: the Torah as the Samaritan community has transmitted it,
/// from a community that separated before the Masoretic text was fixed.
///
/// It is the first witness in this corpus that disagrees with BHSA about what the text says, in the
/// same language. Every other text here is either a translation, or another edition of the same
/// Greek — this one has a word the Masoretic has not, or lacks one it has, in about two verses in
/// five of the Pentateuch, and a <c>link</c> row with one empty side is what that was built for.
///
/// <para>
/// Its <c>textual_family</c> is the first value here that is neither Masoretic nor Mixed. The
/// Samaritan recension is a Hebrew textual family of its own — not a Masoretic manuscript with
/// errors in it — and filing it under either of the existing values would say the opposite of what
/// the text is worth holding for.
/// </para>
/// </summary>
internal static class SamaritanTextSource
{
    public const string Slug = "sp";

    /// <summary>
    /// CC BY-NC 4.0, read from the DT-UCPH repository on 2026-09-04. The repository states its
    /// terms in four places and they do not all agree — the header of the data files says
    /// NonCommercial, the README badge says NonCommercial, the Zenodo deposit says plain
    /// Attribution, and there is no LICENSE file at all. The statement inside the bytes is the one
    /// taken and it is also the stricter of the two that disagree, so believing it costs nothing.
    /// Resources/SamaritanPentateuch/LICENCE.md quotes all four and names the commit read.
    /// </summary>
    public static readonly TextDefinition Definition = new(
        Slug: Slug,
        Name: "The Samaritan Pentateuch",
        NameNative: "תורה",
        Kind: TextKind.ManuscriptTradition,
        Language: "hbo",
        Direction: TextDirection.RightToLeft,
        Versification: Versification.Original,
        PublishedYear: 2018,
        SourceUrl: "https://github.com/DT-UCPH/sp",
        RightsHolder: "Christian Canu Højgaard, Martijn Naaijer and Stefan Schorch",
        Licence: "CC-BY-NC-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-nc/4.0/",
        Redistribution: Redistribution.NonCommercialOnly,
        TextualFamily: "Samaritan")
    {
        Editors = "Stefan Schorch, with Evelyn Burkhardt, Ulrike Hirschfelder, Irina Wandrey and "
                  + "József Zsengellér; encoded for Text-Fabric by Christian Canu Højgaard, Saulo "
                  + "de Oliveira Cantanhêde and Martijn Naaijer",
        About = "The Torah as the Samaritan community has transmitted it, which is not the "
                + "Masoretic text with mistakes in it but a third Hebrew textual family beside the "
                + "Masoretic and the Qumran scrolls, preserved by a community that separated from "
                + "Judaism before the Masoretic text was fixed. The text is the Samaritanus project "
                + "at Martin-Luther-Universität Halle-Wittenberg under Stefan Schorch, transcribed "
                + "from MS Dublin Chester Beatty Library 751 for Genesis to Deuteronomy 32:36 and "
                + "MS Garizim 1 for the remainder, and published as a critical editio maior. It is "
                + "consonantal: the Samaritan tradition writes no vowel points, so what is here is "
                + "the whole of the text rather than one reading of it. Nobody translated it; what "
                + "is edited is the transcription and the annotation.",
        RightsNote = "The dataset states its terms in four places and they do not all agree. The "
                     + "data files' own headers and the README badge say Attribution-NonCommercial "
                     + "4.0; the Zenodo deposit says Attribution 4.0; there is no LICENSE file. The "
                     + "file headers are closest to the bytes and are the stricter, so they are "
                     + "what this is held under. Nothing in the repository claims ShareAlike.",

        // The README asks for the papers as well as the deposit, and a licence name cannot carry
        // that. Both are here because both were asked for.
        Citation = "Christian Canu Højgaard, Martijn Naaijer & Stefan Schorch, Text-Fabric Dataset "
                   + "of the Samaritan Pentateuch, Zenodo, https://doi.org/10.5281/zenodo.7734632; "
                   + "Naaijer, M., Højgaard, C. C., Schorch, S., & Ehrensvärd, M. (2024), "
                   + "Text-Fabric Dataset of the Samaritan Pentateuch, Research Data Journal for "
                   + "the Humanities and Social Sciences 9(1), 1-13, "
                   + "https://doi.org/10.1163/24523666-bja10051",
    };

    public static TextSource Read(string folder) => Build(SamaritanProject.Load(folder));

    public static TextSource Build(SamaritanProject project)
    {
        var books = new List<BookDraft>(project.Books.Count);
        var position = 0;

        foreach (var book in project.Books)
        {
            position++;
            var canonical = BibleBookAbbreviation.GetAbbreviation(book.Name)
                            ?? throw new InvalidOperationException(
                                $"The Samaritan Pentateuch names a book \"{book.Name}\" that has no canonical " +
                                $"ordinal. Add it to {nameof(BibleBookAbbreviation)} — until then this text "
                                + "cannot be placed beside any other.");

            books.Add(new BookDraft(
                CanonicalOrdinal: canonical.Ordinal,
                Position: position,
                Name: canonical.FullName.Full,
                Slug: Slugs.Of(canonical.StandardAbbreviation.Full),
                Chapters: [.. book.Chapters.Select(chapter => new ChapterDraft(
                    chapter.Number,
                    [.. chapter.Verses.Select(Draft)]))],
                Abbreviation: canonical.StandardAbbreviation.Full));
        }

        // The edition is Schorch's and the encoding is the CACCHT project's, and the release number
        // is the only thing that tells two downloads of the same repository apart — so it is read
        // off the files rather than written down here.
        return new TextSource(
            Definition with
            {
                Edition = "Schorch, The Samaritan Pentateuch: A critical editio maior, "
                          + $"in the Text-Fabric encoding {project.Version}",
                EditionYear = 2026,
            },
            books);
    }

    private static VerseDraft Draft(SamaritanVerse verse) =>
        new(verse.Number, [.. verse.Words.Select(Draft)]);

    private static WordDraft Draft(SamaritanWord word) => new(
        Surface: word.Consonants,
        Trailer: word.Trailer,
        Lemma: word.Lexeme,
        StrongNumber: null,
        Gloss: word.Gloss,
        Morphology: Morphology(word),
        Elided: word.Consonants.Length == 0);

    /// <summary>
    /// The annotation, in the keys the rest of the corpus already uses, so that a Samaritan word
    /// and a BHSA word answer the same questions. Two things live here that no other text carries:
    /// the morpheme segmentation, which is the dataset's own contribution and the reason a prefix
    /// on this side can be compared with a prefix on the other; and whether the parsing was carried
    /// over from the Masoretic text rather than established here, which is the difference between
    /// annotation that is evidence about this witness and annotation that is evidence about the
    /// other one.
    /// </summary>
    private static string Morphology(SamaritanWord word)
    {
        var features = new Dictionary<string, string>(20);

        // The dataset says "Hebrew" where BHSA says "hbo", and a reader asking one question of two
        // texts should not have to know which said it.
        Add(features, "language", word.Language is "Hebrew" ? "hbo" : word.Language);
        Add(features, "pos", word.PartOfSpeech);
        Add(features, "gender", word.Gender);
        Add(features, "number", word.Number);
        Add(features, "person", word.Person);
        Add(features, "tense", word.Tense);
        Add(features, "suffixGender", word.SuffixGender);
        Add(features, "suffixNumber", word.SuffixNumber);
        Add(features, "suffixPerson", word.SuffixPerson);

        Add(features, "preformative", word.Morphemes.Preformative);
        Add(features, "verbalStem", word.Morphemes.VerbalStem);
        Add(features, "realizedLexeme", word.Morphemes.Lexeme);
        Add(features, "verbalEnding", word.Morphemes.VerbalEnding);
        Add(features, "nominalEnding", word.Morphemes.NominalEnding);
        Add(features, "univalentFinal", word.Morphemes.UnivalentFinal);
        Add(features, "pronominalSuffix", word.Morphemes.PronominalSuffix);

        if (word.ParsedFromMasoretic)
        {
            features["parsedFromMasoretic"] = "true";
        }

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
