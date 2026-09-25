using Essenthos.Core.Loading;
using Essenthos.Core.Swete;

namespace Essenthos.Core.Alexandrinus;

/// <summary>
/// The books in which Swete's Septuagint prints Codex Alexandrinus rather than Vaticanus, read from
/// First1KGreek's encoding of them, and only as far as the page is Alexandrinus.
///
/// <para>
/// In Genesis Vaticanus is lost up to 46:28, so Swete prints Alexandrinus from the beginning to the
/// word before which his sign § says Vaticanus begins, and Vaticanus after it; only the first part is
/// read. Where Alexandrinus itself is damaged he fills it from other manuscripts in two ways, and
/// neither is taken: letters he puts in square brackets (Genesis 1:20–25 and 1:29–2:3, which his
/// apparatus says <em>perierunt in A</em>) are left out, and so is a word no letter of which is
/// outside them; and the four passages of the torn leaf, which he prints between the signs ¶ and §
/// against a marginal Α, are left out whole. His ¶ and § elsewhere mark where the other uncials of his
/// apparatus begin and end, and are not text. In 1–4 Maccabees he prints Alexandrinus throughout.
/// </para>
/// </summary>
internal static class SweteAlexandrinus
{
    public const string Folder = "swete";

    private const int Genesis = 1;

    private const char Opens = '¶';

    private const char Closes = '§';

    /// <summary>
    /// The chapter and verse of Genesis in which Swete's text passes from Alexandrinus to Vaticanus,
    /// at the first § of the verse.
    /// </summary>
    private static readonly (string Chapter, string Verse) VaticanusBegins = ("46", "28");

    /// <summary>The verses of Genesis where a passage lost from Alexandrinus's torn leaf opens with ¶.</summary>
    private static readonly HashSet<(string Chapter, string Verse)> TornLeaf =
    [
        ("14", "14"), ("15", "1"), ("15", "16"), ("16", "6"),
    ];

    /// <summary>Each book's number in the TLG catalogue of the Septuagint, with its place in the shared canon.</summary>
    public static readonly IReadOnlyList<(int Work, int Canonical)> Books =
    [
        (Genesis, 1), (23, 73), (24, 74), (25, 80), (26, 81),
    ];

    public static string File(int work) => $"tlg0527.tlg{work:000}.1st1K-grc1.xml";

    public static IReadOnlyList<SweteChapter> Read(string folder, int work)
    {
        var path = Path.Combine(folder, Folder, File(work));
        if (!System.IO.File.Exists(path))
        {
            throw new InvalidOperationException(
                $"{path} is missing. Fetch it from First1KGreek's data/tlg0527 with scripts/fetch-alexandrinus.ps1; "
                + "loading the codex without it would leave out a book it has.");
        }

        return SweteReader.Read(Extant(First1KGreekReader.Lines(path, work), work == Genesis)).Chapters;
    }

    /// <summary>The lines of the edition that are Alexandrinus, with the editor's signs taken out.</summary>
    public static IEnumerable<string> Extant(IEnumerable<string> lines, bool genesis)
    {
        var bracketed = false;
        var torn = false;
        (string Chapter, string Verse, string Number)? renumbered = null;

        foreach (var line in lines)
        {
            var space = line.IndexOf(' ');
            var reference = line[..space].Split('.');
            var place = (Chapter: reference[1], Verse: reference[2]);
            var token = line[(space + 1)..];
            var address = renumbered is var (chapter, verse, number) && place == (chapter, verse)
                ? $"{reference[0]}.{chapter}.{number}"
                : line[..space];

            if (token[0] is Opens or Closes)
            {
                if (genesis && token[0] == Closes && place == VaticanusBegins)
                {
                    yield break;
                }

                if (genesis && token[0] == Opens && TornLeaf.Contains(place))
                {
                    torn = true;
                }
                else if (token[0] == Closes)
                {
                    torn = false;
                }

                if (token.Skip(1).All(c => char.IsDigit(c) || c is Opens or Closes))
                {
                    // The transcription lost a verse division here and kept its number beside the sign.
                    if (token.Skip(1).Where(char.IsDigit).ToArray() is { Length: > 0 } digits)
                    {
                        renumbered = (place.Chapter, place.Verse, new string(digits));
                    }

                    continue;
                }

                token = token.TrimStart(Opens, Closes);
            }

            var kept = new System.Text.StringBuilder(token.Length);
            var outside = 0;
            foreach (var c in token)
            {
                switch (c)
                {
                    case '[':
                        bracketed = true;
                        continue;
                    case ']':
                        bracketed = false;
                        continue;
                }

                if (!bracketed && char.IsLetter(c))
                {
                    outside++;
                }

                kept.Append(c);
            }

            if (torn || outside == 0 && kept.ToString().Any(char.IsLetter))
            {
                continue;
            }

            if (kept.Length > 0)
            {
                yield return $"{address} {kept}";
            }
        }
    }
}
