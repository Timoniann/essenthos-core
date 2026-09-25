using Essenthos.Core.Database.Entities;
using Essenthos.Core.Usfm;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Loading;

/// <summary>
/// The title of Psalm 7 as Ohienko printed it.
///
/// <para>
/// Two digitisations of Ohienko circulate and bible4u's is the one that lost the psalm's second line.
/// It also spells the title <c>Жалібна … веніямінівця Куща</c>, where every complete copy, and the
/// transcription of the 1988 printing the lost line is restored from, reads <c>Жалобна … веніяминівця
/// Куша</c>. Once the line was restored the verse read the complete printing in its second half and
/// the defective family in its first; the title is taken from the same transcription so the verse is
/// one edition's.
/// </para>
///
/// <para>
/// Only this verse. The rest of the file differs from the transcription in 2,401 verses, most of it
/// capitals and some of it wording, and which of the two the Ukrainian should come from is a larger
/// question than a title. The transcription's verse is taken whole only where it has as many words as
/// the file's, so the correction is the same words spelled otherwise and every word keeps its row.
/// </para>
/// </summary>
internal static class OhienkoCorrections
{
    private const int Psalms = 19;

    private const int Psalm = 7;

    private const int Title = 1;

    public const string Note =
        "Modified: the title of Psalm 7 is spelled as the 1988 printing spells it — Жалобна, веніяминівця, "
        + "Куша, where this digitisation writes Жалібна, веніямінівця, Куща — from its transcription on Ukrainian "
        + "Wikisource, " + LostVerseEndings.OhienkoSource + ", CC BY-SA 4.0, without its stress marks.";

    public static readonly TextPartSource Source = LostPsalmOpenings.OhienkoSource with
    {
        Covers = "The first line of Psalm 7, which the file the text is loaded from lost, and the spelling of the "
                 + "psalm's title; their stress marks are removed.",
    };

    /// <summary>The correction, or none where the transcription has not been fetched into <paramref name="folder"/>.</summary>
    public static TextRepairs Read(XmlBible.XmlBible bible, string folder)
    {
        if (LostPsalmOpenings.Book(folder, "PSA") is not { } file)
        {
            return TextRepairs.None(Bible4uTextSource.Ohienko);
        }

        var digitised = bible.Books
            .Where(book => Bible4uTextSource.Canonical(book, Bible4uTextSource.Ohienko) == Psalms)
            .SelectMany(book => book.Chapters.Where(chapter => chapter.CNumber == Psalm))
            .SelectMany(chapter => chapter.Verses.Where(verse => verse.VNumber == Title))
            .Select(verse => verse.Text)
            .FirstOrDefault();
        var words = UsfmReader.Read(File.ReadAllText(file))
            .Chapters.FirstOrDefault(chapter => chapter.Number == Psalm)?
            .Verses.FirstOrDefault(verse => verse.Number == Title && verse.Label.Length == 0)?
            .Words;

        if (digitised is null || words is null)
        {
            throw new InvalidOperationException(
                $"Psalm {Psalm}:{Title} is missing from the Ohienko file or from {file}. Either changed since the "
                + "title was compared; run scripts/fetch-ohienko-wikisource.ps1 and look at the verse before loading.");
        }

        var printed = LostVerseEndings.InTheLoadedPunctuation(string.Concat(words.Select(w => w.Surface + w.Trailer)));
        if (VerseWords.Parse(printed).Count != VerseWords.Parse(digitised).Count)
        {
            throw new InvalidOperationException(
                $"The transcription's Psalm {Psalm}:{Title} (\"{printed}\") is not the same words as the file's "
                + $"(\"{digitised}\") spelled otherwise, which is the only correction taken from it here.");
        }

        return printed == digitised
            ? TextRepairs.None(Bible4uTextSource.Ohienko)
            : new TextRepairs(
                Bible4uTextSource.Ohienko, [new VerseRepair(Psalms, Psalm, Title, digitised, printed)], Note, Source);
    }
}
