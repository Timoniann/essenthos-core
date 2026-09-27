using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Swete;

namespace Essenthos.Core.Loading;

/// <summary>
/// The Old Greek of Susanna, Daniel and Bel, which Swete prints beside Theodotion's under the heading
/// <em>κατὰ τοὺς Ο΄</em>, "according to the Seventy".
///
/// <para>
/// A witness of its own rather than three more books of <see cref="SweteTextSource"/>, because it is
/// another translation of the same books. The Septuagint's first Greek Daniel was displaced in the
/// churches by Theodotion's, which is the one Codex Vaticanus reads and so the one Swete prints as his
/// text; the older survives in one Greek manuscript, Codex Chisianus, and Swete prints it from there.
/// The two agree in places word for word and in others tell a different story — Daniel 4 to 6 most of
/// all — so they are two texts at one address, which is what the corpus holds a second text for.
/// </para>
///
/// <para>
/// The files are the same transcription's as Swete's, read by the same reader with the same
/// corrections, and its identifier is this project's own, after <c>SWETE</c>.
/// </para>
/// </summary>
internal static class SweteOldGreekTextSource
{
    public const string Slug = Sources.SweteOldGreekSlug;

    /// <summary>The files, in the order Swete prints them, with each one's place in the shared canon.</summary>
    private static readonly (string File, int Canonical)[] Canon =
    [
        ("54.Susanna_translatio_Graeca", 77),
        ("56.Daniel_translatio_Graeca", 27),
        ("58.Bel_et_Draco_translatio_Graeca", 78),
    ];

    /// <summary>What the text's row says about the corrections, which are made as in Swete's own books.</summary>
    private const string Corrected =
        "Letters corrected by Essenthos: a Latin letter the transcription wrote for the Greek one it looks like "
        + "is written as Greek, and the verse numbers it let into the text in figures are taken out and the "
        + "words kept; where a figure also took a word's first letters in the song of Daniel 3, the word is "
        + "written as the verses on either side print it.";

    /// <summary>The licence is Swete's, whose transcription this is.</summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Swete's Old Greek Daniel",
        NameNative: "ΔΑΝΙΗΛ ΚΑΤΑ ΤΟΥΣ Ο΄",
        Kind: TextKind.CriticalEdition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 1894,
        SourceUrl: "https://github.com/nathans/lxx-swete",
        RightsHolder: "Nathan D. Smith, and the Open Greek and Latin steering committee, over the "
                      + "machine-readable edition. Nobody holds the text itself.",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Septuagint")
    {
        Editors = "Henry Barclay Swete (1835-1917), Regius Professor of Divinity at Cambridge",
        Edition = "The Old Testament in Greek according to the Septuagint, volume 3, Cambridge University Press, "
                  + "1894; transcribed from the printing of 1905",
        About = "Daniel as the Septuagint first had it, with Susanna before it and Bel and the Dragon after. "
                + "The Greek churches came to read another translation of these books, Theodotion's — the "
                + "Daniel of Swete's main text — and this older one all but disappeared: Swete knew it from "
                + "a single Greek manuscript, Codex Chisianus, and prints it beside Theodotion's. The two tell "
                + "the same stories in different words and sometimes differently: this Susanna is shorter, "
                + "and Daniel 4 to 6 follow another telling. It was made in the second or first century BC, "
                + "by a translator whose name is not known. The apparatus is not here; only the text is, "
                + "and it carries no annotation of any kind.",
        RightsNote = "Two rights questions and they have different answers. Swete's text is out of "
                     + "copyright everywhere: he died in 1917 and the volume was printed in 1894 and 1905. "
                     + "What carries a licence is the transcription — Open Greek and Latin photographed and "
                     + "corrected the pages for its First1KGreek project, and Nathan D. Smith converted "
                     + "that into the one-token-per-line files loaded here — and both state Creative "
                     + "Commons Attribution-ShareAlike 4.0, an obligation to credit this digitisation and "
                     + "to share alike anything derived from it. " + Corrected,
        Citation = "Henry Barclay Swete (ed.), The Old Testament in Greek according to the Septuagint, "
                   + "volume 3, Cambridge University Press, 1894, the Old Greek of Susanna, Daniel and Bel "
                   + "(κατὰ τοὺς Ο΄), in the digital edition of Nathan D. Smith (nathans/lxx-swete) derived "
                   + "from the Open Greek and Latin First1KGreek transcription (tlg0527), CC BY-SA 4.0.",
    };

    public static TextSource Read(string folder)
    {
        var books = new List<BookDraft>(Canon.Length);
        foreach (var (file, canonical) in Canon)
        {
            var path = Path.Combine(folder, SweteTextSource.FileName(file));
            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{Slug} is missing {Path.GetRelativePath(folder, path)}. Run scripts/fetch-swete.ps1, which "
                    + "fetches the Old Greek with the rest of Swete's edition.");
            }

            var read = SweteReader.Read(SweteRestorations.Apply(file, SweteDivisions.Lines(file, File.ReadLines(path))));
            books.Add(new BookDraft(
                CanonicalOrdinal: canonical,
                Position: books.Count + 1,
                Name: BookReferences.Name(canonical),
                Slug: BookReferences.Slug(canonical),
                Abbreviation: BookReferences.Abbreviation(canonical),
                Chapters: [.. read.Chapters.Select(chapter => new ChapterDraft(
                    chapter.Number,
                    [.. chapter.Verses.Select(verse => new VerseDraft(
                        verse.Number,
                        [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
                        verse.Label))]))]));
        }

        return new TextSource(Definition, books);
    }
}
