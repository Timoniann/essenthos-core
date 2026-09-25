using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading;

/// <summary>
/// The words bible4u's Synodal prints wrong, written out one by one in <c>SynodalCorrections.json</c>
/// by <c>scripts/synodal-corrections.py</c>, each where the Strong-tagged digitisation of the same
/// 1876 translation (Resources/SynodalStrong) prints it right.
///
/// <para>
/// Four kinds. Words run together, <c>Тирянинбыл</c>, where the tagged edition prints the same
/// letters as separate words: every one of them in five chapters (Joshua 4, 1 Kings 7, Esther 6,
/// Isaiah 28 and 51) and one more at Mark 10:28, and nowhere a spelling the file uses elsewhere —
/// <c>оттого</c> and <c>Заиорданскою</c> are that edition's orthography and stay. A dash or a hyphen
/// the digitisation wrote as a Cyrillic letter in the same chapters: <c>г</c> standing alone where the
/// tagged edition prints a dash, <c>изгза</c> for <c>из-за</c>. Letters, where a word exists in
/// neither edition: <c>произщшли</c>, <c>ОТРАСЛЪ</c> with a hard sign, <c>Бой мой</c> for <c>Бог
/// мой</c>. And four names the file prints in lower case.
/// </para>
///
/// <para>
/// Each correction names the whole token it replaces, so nothing is made anywhere it was not: a
/// token the verse does not hold stops the read, because then the file is not the one the list was
/// checked against.
/// </para>
/// </summary>
internal static class SynodalCorrections
{
    private const string Resource = "Essenthos.Core.Loading.SynodalCorrections.json";

    /// <summary>What the text's row says about the corrections, on a cold load and on a warm one.</summary>
    public const string Note =
        "Corrected by Essenthos: 209 words bible4u's file prints run together, with a dash written as a "
        + "letter, misspelt or with a name in lower case are written as the Synodal prints them, each where "
        + "the Strong-tagged digitisation of the same translation (swmail/RST) reads it so.";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<SynodalCorrection> All = ReadList();

    public static TextRepairs Read(XmlBible.XmlBible bible)
    {
        var verses = bible.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses.Select(verse =>
                (Address: (Bible4uTextSource.Canonical(book, Bible4uTextSource.Synodal), chapter.CNumber, verse.VNumber),
                    verse.Text))))
            .ToDictionary(verse => verse.Address, verse => verse.Text);

        var repairs = new List<VerseRepair>();
        foreach (var here in All.GroupBy(c => (c.Book, c.Chapter, c.Verse)))
        {
            if (!verses.TryGetValue(here.Key, out var digitised))
            {
                throw new InvalidOperationException(
                    $"The Synodal has no verse {here.Key} to correct. The file is not the one the corrections were " +
                    "drawn up against; run scripts/synodal-corrections.py over it before loading.");
            }

            var printed = digitised;
            foreach (var correction in here.OrderByDescending(c => c.Digitised.Length))
            {
                var token = Token(correction.Digitised);
                if (!token.IsMatch(printed))
                {
                    throw new InvalidOperationException(
                        $"The Synodal's {here.Key} does not print \"{correction.Digitised}\", which the corrections " +
                        "replace there. The file is not the one they were drawn up against; run " +
                        "scripts/synodal-corrections.py over it before loading.");
                }

                printed = token.Replace(printed, correction.Printed);
            }

            repairs.Add(new VerseRepair(here.Key.Book, here.Key.Chapter, here.Key.Verse, digitised, printed));
        }

        return new TextRepairs(Bible4uTextSource.Synodal, repairs, Note);
    }

    /// <summary>A whole token, so <c>г</c> never matches inside <c>гчетыре</c> or a word it begins.</summary>
    private static Regex Token(string digitised) => new($@"(?<![\w-]){Regex.Escape(digitised)}(?!\w)");

    private static IReadOnlyList<SynodalCorrection> ReadList()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<List<SynodalCorrection>>(stream, Shape) ?? [];
    }
}

/// <param name="Kind">glued, dash, letters or case.</param>
internal sealed record SynodalCorrection(int Book, int Chapter, int Verse, string Kind, string Digitised, string Printed);
