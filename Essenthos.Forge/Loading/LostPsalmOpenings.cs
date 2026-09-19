using System.Globalization;
using System.Text;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// Where the psalm openings a loaded text does not print are read from: a complete copy of the same
/// edition, fetched for the purpose and kept beside the data with its licence.
///
/// Each of the two is one method rather than one class, because what the corpus asks of either is
/// one question — <em>which words does this text lack at the head of which psalm</em> — and the
/// answer is a list the loader writes, not a text anything loads.
/// </summary>
internal static class LostPsalmOpenings
{
    /// <summary>
    /// The folder <c>scripts/fetch-ebible.ps1</c> writes eBible's <c>eng-kjv2006</c> into, and the
    /// folder <c>scripts/fetch-ohienko-wikisource.ps1</c> writes uk.wikisource's Ohienko into.
    /// </summary>
    public const string KingJamesFolder = "KingJames2006";

    public const string OhienkoFolder = "OhienkoWikisource";

    /// <summary>
    /// The King James superscriptions: 116 of them, 1,034 words, each word tagged with the Hebrew
    /// word it renders.
    ///
    /// They are read out of the standardised 1769 text eBible publishes, which was compared with
    /// the King James this corpus serves verse by verse before any of it was used: 22,154 of the
    /// 31,102 verses are the same character for character, and of the rest all but 389 differ only
    /// by the casing of the divine name, a pilcrow, a hyphen in a proper name or punctuation.
    /// Resources/KingJames2006/LICENCE.md has that table and what the 389 are.
    /// </summary>
    public static IReadOnlyList<PsalmOpening> KingJamesSuperscriptions(string folder)
    {
        var file = Psalms(folder);
        if (file is null)
        {
            return [];
        }

        return
        [
            .. UsfmReader.Superscriptions(File.ReadAllText(file))
                .OrderBy(title => title.Key)
                .Select(title => new PsalmOpening(
                    title.Key,
                    PsalmOpeningPlace.BeforeTheVerse,
                    [
                        .. title.Value.Select(word => new WordDraft(
                            word.Surface, word.Trailer, StrongNumber: word.StrongNumber)),
                    ])),
        ];
    }

    /// <summary>
    /// The one verse Ohienko printed that the digitisation this corpus loaded him from dropped:
    /// Psalm 7's first line, <em>Господи, Боже мій, — я до Тебе вдаюся</em>, which stands between
    /// the psalm's title and what the loaded file numbers as its second verse.
    ///
    /// It is appended to the title rather than written as a verse, because that is what every other
    /// psalm of this text does — bible4u prints title and first line inside verse 1 throughout, and
    /// Psalm 7 is the one place where the line went missing and the title was left standing alone.
    ///
    /// **Addressed by the fetched file's own numbering, which is not the corpus's.** uk.wikisource
    /// numbers the psalms as the Septuagint does and the Old Testament as the Hebrew does, so its
    /// chapter 7 is only the corpus's Psalm 7 because those two schemes happen to agree before
    /// Psalm 9. Nothing here converts between them, and nothing else may be taken from that file
    /// without a versification for it.
    /// </summary>
    public static IReadOnlyList<PsalmOpening> OhienkoLostLine(string folder)
    {
        const int psalm = 7;
        const int lostVerse = 2;

        var file = Psalms(folder);
        if (file is null)
        {
            return [];
        }

        var words = UsfmReader.Read(File.ReadAllText(file))
            .Chapters.FirstOrDefault(chapter => chapter.Number == psalm)?
            .Verses.FirstOrDefault(verse => verse.Number == lostVerse)?
            .Words;

        if (words is null)
        {
            throw new InvalidOperationException(
                $"{file} has no Psalm {psalm}:{lostVerse}, which is the verse Ohienko's own printing has and " +
                "the loaded text does not. Either the transcription changed or the wrong file is here; run " +
                "scripts/fetch-ohienko-wikisource.ps1 rather than writing a verse that is not in it.");
        }

        return
        [
            new PsalmOpening(
                psalm,
                PsalmOpeningPlace.AfterTheVerse,
                [.. words.Select(word => new WordDraft(Unaccented(word.Surface), word.Trailer))]),
        ];
    }

    private static string? Psalms(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder, "*.usfm")
            .FirstOrDefault(path => File.ReadLines(path).FirstOrDefault()?
                .StartsWith("\\id PSA", StringComparison.Ordinal) == true);
    }

    /// <summary>
    /// Drops the stress marks the transcription carries. They are an apparatus for a reader saying
    /// the word aloud — Ohienko's printing has them and every other Ukrainian file here has none —
    /// and a word that kept them would match nothing the reader types and nothing the rest of the
    /// text spells the same way.
    /// </summary>
    private static string Unaccented(string surface)
    {
        var decomposed = surface.Normalize(NormalizationForm.FormD);
        var kept = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                kept.Append(character);
            }
        }

        return kept.ToString().Normalize(NormalizationForm.FormC);
    }
}
