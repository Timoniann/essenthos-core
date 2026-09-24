using System.Text.RegularExpressions;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <param name="Book">The book, by its canonical ordinal.</param>
/// <param name="Complete">
/// The whole verse as the complete edition prints it, written in the loaded text's punctuation, so
/// that what the loaded verse holds is a prefix of it and the rest is what was lost.
/// </param>
internal sealed record VerseEnding(int Book, int Chapter, int Verse, string Complete);

/// <summary>
/// Where the ends of verses a loaded text cut short are read from: a complete copy of the same
/// edition, kept beside the data with its licence.
/// </summary>
internal static partial class LostVerseEndings
{
    /// <summary>The page of uk.wikisource every book of the transcription hangs from.</summary>
    public const string OhienkoSource = "https://uk.wikisource.org/wiki/Біблія_(Огієнко)";

    private const string OldTestament = "Біблія (Огієнко)/Книги Старого Заповіту/";

    /// <param name="Code">The book's code in the transcription's <c>\id</c> line.</param>
    /// <param name="Page">The uk.wikisource page the book was read from.</param>
    /// <param name="Lost">What the King James reads where the loaded verse stops, which is the sense check.</param>
    private sealed record Truncation(string Code, int Book, int Chapter, int Verse, string Page, string Lost);

    /// <summary>
    /// Every verse of bible4u's Ohienko that stops where his printing goes on. Found by comparing
    /// every verse outside the Psalms with the transcription: in these seven, and no others, the
    /// loaded verse is the head of the printed one, the rest stands in no following verse of the
    /// loaded file, and the King James reads the same sense in its place.
    ///
    /// Taken by address, which is safe only because the loader requires the loaded verse to be the
    /// head of the one read here: the transcription numbers the Old Testament as the Hebrew does,
    /// and a verse the two schemes number differently would not begin with the same words.
    /// </summary>
    private static readonly Truncation[] Ohienko =
    [
        new("GEN", 1, 22, 19, OldTestament + "Перша книга Мойсеєва: Буття",
            "and they rose up and went together to Beer-sheba; and Abraham dwelt at Beer-sheba."),
        new("GEN", 1, 44, 26, OldTestament + "Перша книга Мойсеєва: Буття",
            "if our youngest brother be with us, then will we go down: for we may not see the man's face, "
            + "except our youngest brother be with us."),
        new("GEN", 1, 50, 11, OldTestament + "Перша книга Мойсеєва: Буття",
            "This is a grievous mourning to the Egyptians: wherefore the name of it was called "
            + "Abel-mizraim, which is beyond Jordan."),
        new("2SA", 10, 17, 20, OldTestament + "Друга книга Самуїлова (або Друга книга царів)",
            "They be gone over the brook of water. And when they had sought and could not find them, "
            + "they returned to Jerusalem."),
        new("JOB", 18, 2, 2, OldTestament + "Книга Йова",
            "From going to and fro in the earth, and from walking up and down in it."),
        new("ISA", 23, 50, 9, OldTestament + "Книга пророка Ісаї",
            "lo, they all shall wax old as a garment; the moth shall eat them up."),
        new("HAB", 35, 1, 8, OldTestament + "Книга пророка Авакума",
            "and their horsemen shall spread themselves, and their horsemen shall come from far; they "
            + "shall fly as the eagle that hasteth to eat."),
    ];

    /// <summary>What the text's row says about these words once they are written.</summary>
    public const string OhienkoNote =
        "Modified: the ends of seven verses this digitisation cut short — Genesis 22:19, 44:26 and "
        + "50:11, 2 Samuel 17:20, Job 2:2, Isaiah 50:9 and Habakkuk 1:8 — are restored from the "
        + "transcription of the 1988 printing on Ukrainian Wikisource, " + OhienkoSource + ", "
        + "CC BY-SA 4.0, without the stress marks, quotation marks and dashes this text prints nowhere.";

    /// <summary>
    /// Ohienko's seven verses, whole, from the transcription in <paramref name="folder"/>; empty
    /// where it has not been fetched.
    /// </summary>
    public static IReadOnlyList<VerseEnding> OhienkoVerses(string folder)
    {
        var endings = new List<VerseEnding>(Ohienko.Length);
        foreach (var book in Ohienko.GroupBy(t => t.Code))
        {
            var file = LostPsalmOpenings.Book(folder, book.Key);
            if (file is null)
            {
                return [];
            }

            var chapters = UsfmReader.Read(File.ReadAllText(file)).Chapters;
            foreach (var truncation in book)
            {
                var words = chapters.FirstOrDefault(c => c.Number == truncation.Chapter)?
                    .Verses.FirstOrDefault(v => v.Number == truncation.Verse && v.Label.Length == 0)?
                    .Words
                    ?? throw new InvalidOperationException(
                        $"{file} has no {truncation.Code} {truncation.Chapter}:{truncation.Verse}, which is a verse " +
                        "the loaded Ohienko cuts short. Either the transcription changed or the wrong file is " +
                        "here; run scripts/fetch-ohienko-wikisource.ps1 rather than writing a verse that is not in it.");

                endings.Add(new VerseEnding(
                    truncation.Book,
                    truncation.Chapter,
                    truncation.Verse,
                    InTheLoadedPunctuation(string.Concat(words.Select(w => w.Surface + w.Trailer)))));
            }
        }

        return endings;
    }

    /// <summary>
    /// The transcription's verse as bible4u writes Ohienko: no stress marks, no quotation marks and
    /// no dashes, none of which the loaded file prints anywhere. Keeping them would leave a closing
    /// quotation mark in the restored words whose opening one the loaded head does not have.
    /// </summary>
    internal static string InTheLoadedPunctuation(string verse)
    {
        var text = LostPsalmOpenings.Unaccented(verse);
        text = Quotes().Replace(text, string.Empty);
        text = Dash().Replace(text, string.Empty);
        text = SpaceBeforePunctuation().Replace(text, "$1");
        text = Whitespace().Replace(text, " ").Trim();

        if (Latin().IsMatch(text))
        {
            throw new InvalidOperationException(
                $"\"{text}\" carries a Latin letter, which is how the transcription mistypes a Cyrillic one. " +
                "Correct the letter in this reader before writing the verse; do not load it as it stands.");
        }

        return text;
    }

    [GeneratedRegex("[„“”«»]")]
    private static partial Regex Quotes();

    [GeneratedRegex(@"\s*—")]
    private static partial Regex Dash();

    [GeneratedRegex(@"\s+([.,:;!?])")]
    private static partial Regex SpaceBeforePunctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex Latin();
}
