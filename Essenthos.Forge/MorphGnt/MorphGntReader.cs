namespace Essenthos.Core.MorphGnt;

/// <summary>
/// MorphGNT's parsing files: one word per line, seven space-separated columns, no quoting anywhere
/// and no header.
///
/// <code>
/// 010101 N- ----NSF- Βίβλος Βίβλος βίβλος βίβλος
/// 010102 V- 3AAI-S-- ἐγέννησεν ἐγέννησεν ἐγέννησε(ν) γεννάω
/// </code>
///
/// <para>
/// The address is six digits — book, chapter, verse, two each — counting the New Testament from 01,
/// so Matthew is 01 and Revelation 27. The file names count the same books from 61, which is the
/// old Nestle numbering; neither number is this corpus's canonical ordinal and both are converted
/// at the edge rather than carried.
/// </para>
///
/// <para>
/// The columns are split on whitespace rather than on a tab, because the files use a single space
/// and a reader that assumes a tab returns one column of seven words. Every line has exactly seven,
/// checked here: the format is inherited from CCAT and its README says the codes will change in the
/// next major release, so a file that has quietly grown a column is a thing to stop on rather than
/// to read the first seven fields of.
/// </para>
/// </summary>
internal static class MorphGntReader
{
    private const int Columns = 7;

    /// <summary>
    /// The 27 books as the repository names its files. The number is not the canonical ordinal and
    /// is not the address's book either; it is part of the file name and nothing more.
    /// </summary>
    public static readonly string[] Books =
    [
        "61-Mt", "62-Mk", "63-Lk", "64-Jn", "65-Ac", "66-Ro", "67-1Co", "68-2Co", "69-Ga",
        "70-Eph", "71-Php", "72-Col", "73-1Th", "74-2Th", "75-1Ti", "76-2Ti", "77-Tit", "78-Phm",
        "79-Heb", "80-Jas", "81-1Pe", "82-2Pe", "83-1Jn", "84-2Jn", "85-3Jn", "86-Jud", "87-Re"
    ];

    /// <summary>Matthew is the fortieth book of the canon and the first of these.</summary>
    private const int FirstNewTestamentBook = 40;

    public static string File(string folder, string book) =>
        Path.Combine(folder, "parsing", $"{book}-morphgnt.txt");

    /// <summary>
    /// Every word of one book, in document order, with the address turned into a canonical book
    /// ordinal so that nothing downstream has to know how MorphGNT numbers the New Testament.
    /// </summary>
    public static IReadOnlyList<MorphGntWord> Read(string content)
    {
        var words = new List<MorphGntWord>(6_000);

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            var fields = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != Columns)
            {
                throw new InvalidDataException(
                    $"A MorphGNT line has {fields.Length} columns and the format has {Columns}: " +
                    $"\"{trimmed}\". Either the file is corrupt or the format changed — its README " +
                    "warns that the CCAT codes will be replaced in the next major release. Re-run " +
                    "scripts/fetch-morphgnt.ps1, and if the format did change, the parse-code " +
                    "reader has to change with it before anything is loaded.");
            }

            var address = fields[0];
            if (address.Length != 6 || !address.All(char.IsAsciiDigit))
            {
                throw new InvalidDataException(
                    $"A MorphGNT line addresses its word as \"{address}\", which is not the six " +
                    "digits of book, chapter and verse this format uses. Re-run " +
                    "scripts/fetch-morphgnt.ps1.");
            }

            words.Add(new MorphGntWord(
                Book: int.Parse(address[..2]) + FirstNewTestamentBook - 1,
                Chapter: int.Parse(address[2..4]),
                Verse: int.Parse(address[4..]),
                PartOfSpeech: fields[1],
                Parse: fields[2],
                Printed: fields[3],
                Word: fields[4],
                Normalised: fields[5],
                Lemma: fields[6]));
        }

        return words;
    }

    /// <summary>The whole New Testament, in canonical order.</summary>
    public static IReadOnlyList<MorphGntWord> ReadAll(string folder) =>
        [.. Books.SelectMany(book => Read(System.IO.File.ReadAllText(File(folder, book))))];
}
