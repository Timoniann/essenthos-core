using Essenthos.Core.Corpus;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Microsoft.Data.Sqlite;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Wade Maxfield's Strong numbers on the American Standard Version, as his Cross Word Project ships them: one SQLite
/// file (<c>ASV.bbl</c>), a table per book and a row per English word with the number of the Hebrew or Greek word it
/// renders, or none. The numbers sit on the head word of a phrase (<em>In the beginning</em> numbers only
/// <em>beginning</em>), and in the Old Testament they are the King James's carried over.
///
/// <para>
/// **Read as a numbered edition of the ASV, laid onto the eBible ASV the corpus holds** (<see cref="Conversion.SideStrongNumbers"/>):
/// a supporting layer, ranked below the ASV's composed links (owner, 2026-11-19, TSK-1890). The file's own terms
/// (its <c>copyright</c> table) release the numbering for any purpose; the reader refuses a file that no longer says so.
/// </para>
///
/// <para>
/// **One systematic defect is left out here.** In the New Testament the number of a Greek conjunction or particle that
/// has no English word of its own lands on the last English word of its clause, and that word's own number is lost:
/// Luke 4:12 <em>the Lord thy God</em> numbers <em>God</em> G3754 (ὅτι), Hebrews 11:28 <em>blood</em> G2443 (ἵνα).
/// Such a number on an English word that is not itself a conjunction, particle or other function word is dropped
/// (<c>strong/numbers.tsv</c>, list <c>clause-particle</c>: ὅτι, ἵνα, δέ, διό, γάρ, οὖν, the six the measurement found first,
/// and ten more that behave the same, TSK-1890; negations, ἄρα, ἐπεί and οὕτως are right where they stand): 288 of the file's
/// 120,176 Greek numbers, 100 of them ὅτι's.
/// </para>
/// </summary>
internal static class MaxfieldAsvEdition
{
    /// <summary>Where the fetch script puts the file, under the corpus sources.</summary>
    public static readonly string[] EditionFile = ["AmericanStandard1901Strong", "ASV.bbl"];

    /// <summary>What every link drawn from the numbering begins with, and what the dataset declaration claims.</summary>
    public const string Credit = Sources.MaxfieldAsvStrongCredit;

    /// <summary>The language of the edition's words, whose function words it is read with.</summary>
    public const string Language = "eng";

    /// <summary>
    /// The witnesses the numbers are matched against: the Hebrew, and Nestle 1904 for a New Testament revised on the
    /// critical Greek of 1881. They are the two pairs the ASV has today.
    /// </summary>
    public static readonly string[] Witnesses = [BhsaTextSource.Slug, NestleTextSource.Slug];

    /// <summary>The words of the file's terms the layer is taken under; a file whose <c>copyright</c> table lacks them is refused.</summary>
    public const string Terms = "copyright Wade Maxfield and released by him for any purpose";

    /// <summary>The file's tables in canonical order, one per book.</summary>
    private static readonly string[] Tables =
    [
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "OneSam", "TwoSam",
        "OneKing", "TwoKing", "OneChr", "TwoChr", "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Proverbs",
        "Ecclesiastes", "SOS", "Isaiah", "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea", "Joel", "Amos",
        "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah", "Haggai", "Zechariah", "Malachi",
        "Matthew", "Mark", "Luke", "John", "Acts", "Romans", "OneCor", "TwoCor", "Galatians", "Ephesians",
        "Philippians", "Colossians", "OneThes", "TwoThes", "OneTim", "TwoTim", "Titus", "Philemon", "Hebrews", "James",
        "OnePeter", "TwoPeter", "OneJohn", "TwoJohn", "ThreeJohn", "Jude", "Revelation",
    ];

    /// <summary>The file read verse by verse, with the clause particles' misplaced numbers left out.</summary>
    /// <param name="clauseParticles">The numbers dropped from an English content word (<see cref="StrongNumberLists.ClauseParticles"/>).</param>
    public static Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> Read(
        string path, IReadOnlySet<string> clauseParticles, Func<string, bool> functionWord) =>
        Read(path, clauseParticles, functionWord, out _);

    /// <param name="functionWord">Whether an English word is a function word (<see cref="Mapping.FunctionWords"/>).</param>
    /// <param name="dropped">How many numbers the clause particles left out.</param>
    public static Dictionary<(int Book, int Chapter, int Verse), List<EditionWord>> Read(
        string path, IReadOnlySet<string> clauseParticles, Func<string, bool> functionWord, out int dropped)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Wade Maxfield's ASV Strong numbers are not at {path}: run scripts/fetch-asv-strong.ps1.", path);
        }

        var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        RequireTerms(connection, path);

        var verses = new Dictionary<(int, int, int), List<EditionWord>>(31_200);
        var unit = 0;
        dropped = 0;
        for (var book = 1; book <= Tables.Length; book++)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT Chapter, Verse, Word, OrigWordLanguage, DictEntryNumber FROM \"{Tables[book - 1]}\" ORDER BY Chapter, Verse, WordSeqNumber";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var (chapter, verse) = (reader.GetInt32(0), reader.GetInt32(1));
                var tokens = (reader.IsDBNull(2) ? string.Empty : reader.GetString(2)).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // Chapter 0 holds fragments of the book's title, not words of a verse.
                if (chapter < 1 || verse < 1 || tokens.Length == 0)
                {
                    continue;
                }

                var number = Number(reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? 0 : reader.GetInt64(4));
                if (number is not null && clauseParticles.Contains(number) && !tokens.All(functionWord))
                {
                    number = null;
                    dropped++;
                }

                if (!verses.TryGetValue((book, chapter, verse), out var words))
                {
                    verses[(book, chapter, verse)] = words = [];
                }

                IReadOnlyList<string> numbers = number is null ? [] : [number];
                foreach (var token in tokens)
                {
                    words.Add(new EditionWord(token, numbers, unit));
                }

                unit++;
            }
        }

        return verses;
    }

    /// <summary>
    /// A row's Strong number as the corpus writes it (<c>H7225</c>, <c>G2316</c>), or null. The language column is
    /// <c>H</c> or <c>G</c>, now and then with a trailing return or in lower case; <c>F</c> marks a footnote on the word,
    /// and its number is the footnote's. A number whose language the file does not mark (five words) is left out.
    /// </summary>
    internal static string? Number(string? language, long entry)
    {
        var series = language?.Trim().ToUpperInvariant();
        return entry > 0 && series is "H" or "G" ? series + entry.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    private static void RequireTerms(SqliteConnection connection, string path)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CopyrightInfo FROM copyright";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var said = string.Join(' ', (reader.IsDBNull(0) ? string.Empty : reader.GetString(0)).Split((char[])[' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries));
            if (said.Contains(Terms, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new InvalidDataException(
            $"{path}: its copyright table no longer says \"{Terms}\". Read the terms again and record them in "
            + "Resources/AmericanStandard1901Strong/LICENCE.md before the numbers are used.");
    }
}

/// <summary>
/// English nouns a translator adds to give an adjective, a pronoun or a particle a head (<em>all things</em>, <em>every man</em>,
/// <em>the holy ones</em>, <em>one to another</em>, <em>all manner of</em>, <em>for this cause</em>), which then render no Hebrew or Greek word of their own; the owner's
/// ruling of 2026-11-19 (TSK-1890) keeps the ASV's laid numbers from filling them (<see cref="Mapping.StrongNumberSource.LaidRules"/>).
/// The words are the list <c>added-noun</c> of <c>maxfield-asv</c> in <c>sources/words.tsv</c>.
/// </summary>
internal sealed class EnglishAddedNouns(IReadOnlySet<string> words)
{
    /// <summary>The store's list (<see cref="SourceWords"/>).</summary>
    public static EnglishAddedNouns Of(ProductStore store) =>
        new(SourceWords.Of(store).Set(SourceWords.MaxfieldAsv, SourceWords.AddedNoun, StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether a word, as printed (its case, a possessive ending and any apostrophe aside), is one of them.</summary>
    public bool Contains(string word)
    {
        var bare = word.Replace('’', '\'').Trim('\'', '‘');
        return words.Contains(bare.EndsWith("'s", StringComparison.OrdinalIgnoreCase) ? bare[..^2] : bare);
    }
}
