using Essenthos.Core.Corpus;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Swete;

namespace Essenthos.Core.Loading;

/// <summary>
/// Ottley's Isaiah: Codex Alexandrinus, as Richard Rusden Ottley printed it at Cambridge in 1904.
///
/// <para>
/// A witness of its own rather than a book of Swete's, because it is another manuscript. Swete
/// printed Codex Vaticanus, of the fourth century; Alexandrinus is the other great codex of the Greek
/// Bible, copied in the fifth and from another tradition of the text, and in Isaiah the two differ in
/// small places throughout — word order, an article, a spelling, a line one has and the other has
/// not. This is the only book the corpus holds of it, and it holds it whole.
/// </para>
///
/// <para>
/// Its identifier is this project's own, after the editor as <c>SWETE</c> is: nothing that serves
/// the edition publishes a code for it.
/// </para>
/// </summary>
internal static class OttleyTextSource
{
    public const string Slug = Sources.OttleySlug;

    public const int Isaiah = 23;

    /// <summary>
    /// The licence is the one the transcription states in its own header, which is what reaches the
    /// bytes: <em>"Available under a Creative Commons Attribution-ShareAlike 4.0 International
    /// License"</em>, the same as Swete's. Ottley's text itself, printed in 1904, is out of copyright.
    /// </summary>
    public static TextDefinition Definition => new(
        Slug: Slug,
        Name: "Ottley's Isaiah (Codex Alexandrinus)",
        NameNative: "ΗΣΑΙΑΣ ΠΡΟΦΗΤΗΣ ΙΓ΄",
        Kind: TextKind.ManuscriptTradition,
        Language: "grc",
        Direction: TextDirection.LeftToRight,
        Versification: Versification.Septuagint,
        PublishedYear: 1904,
        SourceUrl: "https://github.com/OpenGreekAndLatin/First1KGreek/blob/"
                   + "b67137e6b82669d08fe6ad1c225999ca6aca362c/data/tlg0527/tlg048/tlg0527.tlg048.1st1K-grc2.xml",
        RightsHolder: "The Open Greek and Latin project at the University of Leipzig, over the transcription. "
                      + "Nobody holds the text itself.",
        Licence: "CC-BY-SA-4.0",
        LicenceUrl: "https://creativecommons.org/licenses/by-sa/4.0/",
        Redistribution: Redistribution.ShareAlike,
        TextualFamily: "Septuagint")
    {
        Editors = "Richard Rusden Ottley",
        Edition = "The Book of Isaiah according to the Septuagint (Codex Alexandrinus), volume 2, Text and "
                  + "Notes, Cambridge University Press, 1904",
        About = "Isaiah as one manuscript has it: Codex Alexandrinus, copied in the fifth century and kept "
                + "in the British Library, printed by Ottley as it stands, with the readings of the other "
                + "manuscripts in notes at the foot of the page, which are not here. Where Swete gives Codex "
                + "Vaticanus, this gives the other great codex of the Greek Bible, so the two can be read "
                + "against each other word by word. Ottley numbers the verses by the Hebrew, and where the "
                + "Greek has nothing that answers to a Hebrew verse he gives it no number: there is no 38:15 "
                + "and no 40:7, and their words, where the Greek has any, stand in the verse beside. Words "
                + "the manuscript's correctors added, and words Ottley marks to be struck out, are printed "
                + "and kept. The manuscript's title, Isaiah the thirteenth prophet, and its closing line "
                + "are not words of a verse.",
        RightsNote = "Ottley's text, printed in 1904, is out of copyright. What "
                     + "carries a licence is the transcription, made for Open Greek and Latin's First1KGreek "
                     + "project, whose header states Creative Commons Attribution-ShareAlike 4.0 — an "
                     + "obligation to credit it and to share alike anything derived from it. "
                     + OttleyIsaiah.Note + " " + OttleyIsaiah.PageNote,
        Citation = "Richard Rusden Ottley (ed.), The Book of Isaiah according to the Septuagint (Codex "
                   + "Alexandrinus), volume 2, Cambridge University Press, 1904, in the Open Greek and Latin "
                   + "First1KGreek transcription (tlg0527.tlg048.1st1K-grc2), CC BY-SA 4.0.",
    };

    public static TextSource Read(string sweteFolder)
    {
        var path = Path.Combine(sweteFolder, SweteIsaiah.Folder, OttleyIsaiah.File);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{Slug} is missing {path}. Run scripts/fetch-swete.ps1, which fetches it beside Swete's "
                + "Isaiah from the same transcription.");
        }

        var read = SweteReader.Read(OttleyIsaiah.Lines(sweteFolder));
        return new TextSource(Definition,
        [
            new BookDraft(
                CanonicalOrdinal: Isaiah,
                Position: 1,
                Name: BookReferences.Name(Isaiah),
                Slug: BookReferences.Slug(Isaiah),
                Abbreviation: BookReferences.Abbreviation(Isaiah),
                Chapters: [.. read.Chapters.Select(chapter => new ChapterDraft(
                    chapter.Number,
                    [.. chapter.Verses.Select(verse => new VerseDraft(
                        verse.Number,
                        [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))],
                        verse.Label))]))]),
        ]);
    }
}
