using Essenthos.Core.Conversion;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// Wade Maxfield's Strong numbers on the American Standard Version (<see cref="MaxfieldAsvEdition"/>), read from a
/// small copy of his SQLite file, laid onto the ASV's words and matched against Nestle 1904 (<see cref="StrongNumberSource"/>):
/// a supporting layer that leaves out a clause particle's number on a content word, a number naming several Greek words
/// nothing pairs off, and links of its own from function words.
/// </summary>
public sealed class MaxfieldAsvStrongTests : IDisposable
{
    private const string Asv = EnglishTextSource.AmericanStandard;

    private const string Pair = Asv + "-" + NestleTextSource.Slug;

    private const string Terms =
        "The American Standard Version was published in 1901 and is in the public domain in the United States.\r\r"
        + "Applying Strong's to the text was done as part of the \"Cross Word Project\" (www.crosswordproject.com) and is  "
        + "copyright Wade Maxfield and released by him for any purpose.\r\r  PREFACE TO THE AMERICAN EDITION";

    private static readonly string[] Books =
    [
        "Genesis", "Exodus", "Leviticus", "Numbers", "Deuteronomy", "Joshua", "Judges", "Ruth", "OneSam", "TwoSam",
        "OneKing", "TwoKing", "OneChr", "TwoChr", "Ezra", "Nehemiah", "Esther", "Job", "Psalms", "Proverbs",
        "Ecclesiastes", "SOS", "Isaiah", "Jeremiah", "Lamentations", "Ezekiel", "Daniel", "Hosea", "Joel", "Amos",
        "Obadiah", "Jonah", "Micah", "Nahum", "Habakkuk", "Zephaniah", "Haggai", "Zechariah", "Malachi",
        "Matthew", "Mark", "Luke", "John", "Acts", "Romans", "OneCor", "TwoCor", "Galatians", "Ephesians",
        "Philippians", "Colossians", "OneThes", "TwoThes", "OneTim", "TwoTim", "Titus", "Philemon", "Hebrews", "James",
        "OnePeter", "TwoPeter", "OneJohn", "TwoJohn", "ThreeJohn", "Jude", "Revelation",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-maxfield", Guid.NewGuid().ToString("N"));

    private readonly ProductStore _store;

    public MaxfieldAsvStrongTests()
    {
        _store = new ProductStore(_root);

        // Genesis 1:1 as the file writes it: the head word numbered, the language now and then with a return after it,
        // a footnote marker on a word, and a chapter 0 holding a piece of the book's title. Luke 4:12 has ὅτι's number on
        // "God", the last word of the clause; 4:13 numbers "God" where the Greek writes θεός twice.
        Edition(Path.Combine([_root, .. MaxfieldAsvEdition.EditionFile]), Terms,
            ("Genesis", [(0, 1, "of", " ", 0), (1, 1, "In", "", 0), (1, 1, "the", "", 0), (1, 1, "beginning", "H", 7225),
                (1, 1, "God", "H\r", 430), (1, 1, "created", "F", 1)]),
            ("Luke", [(4, 12, "Jesus", "G", 2424), (4, 12, "said", "G", 2036), (4, 12, "that", "G", 3754), (4, 12, "but", "g", 1161),
                (4, 12, "the", "", 0), (4, 12, "Lord", "G", 2962), (4, 12, "thy", "G", 4675), (4, 12, "God", "G", 3754),
                (4, 13, "God", "G", 2316), (4, 13, "spake", "G", 2980)]));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Each_word_takes_its_own_number_and_a_footnote_or_a_title_fragment_none()
    {
        var verses = MaxfieldAsvEdition.Read(Path.Combine([_root, .. MaxfieldAsvEdition.EditionFile]));

        verses.Keys.Should().BeEquivalentTo([(1, 1, 1), (42, 4, 12), (42, 4, 13)], "chapter 0 is the book's title, not a verse");
        verses[(1, 1, 1)].Select(word => (word.Text, string.Join(' ', word.Numbers))).Should().Equal(
            ("In", ""), ("the", ""), ("beginning", "H7225"), ("God", "H430"), ("created", ""));
        verses[(1, 1, 1)].Select(word => word.Unit).Should().OnlyHaveUniqueItems("every word is a rendering of its own");
    }

    /// <summary>
    /// Luke 4:12, <em>the Lord thy God</em>: the file numbers <em>God</em> G3754, the ὅτι the English does not render, so the
    /// number goes and <em>God</em> is untagged. The same number on <em>that</em>, which renders it, stays, and so does δέ's on
    /// <em>but</em>, its language written in lower case.
    /// </summary>
    [Fact]
    public void A_clause_particle_s_number_on_a_content_word_is_dropped_and_on_a_function_word_kept()
    {
        var verse = MaxfieldAsvEdition.Read(Path.Combine([_root, .. MaxfieldAsvEdition.EditionFile]), out var dropped)[(42, 4, 12)];

        verse.Single(word => word.Text == "God").Numbers.Should().BeEmpty();
        verse.Single(word => word.Text == "that").Numbers.Should().Equal("G3754");
        verse.Single(word => word.Text == "but").Numbers.Should().Equal("G1161");
        dropped.Should().Be(1);
        MaxfieldAsvEdition.ClauseParticles.Should().Contain(["G3754", "G2443", "G1161", "G1352", "G1063", "G3767"]);
    }

    [Fact]
    public void A_file_whose_copyright_table_no_longer_states_the_terms_is_refused()
    {
        var other = Path.Combine(_root, "other", "ASV.bbl");
        Edition(other, "Copyright Wade Maxfield, all rights reserved.", ("Genesis", [(1, 1, "God", "H", 430)]));

        var read = () => MaxfieldAsvEdition.Read(other);

        read.Should().Throw<InvalidDataException>().WithMessage("*released by him for any purpose*");
    }

    [Fact]
    public void The_ASV_s_numbers_are_laid_from_Maxfield_s_edition_and_matched_against_the_Hebrew_and_Nestle()
    {
        SideStrongNumbers.Edition(Asv).Source.Should().Be("AmericanStandard1901Strong/ASV.bbl");
        StrongNumberSource.All.Should().ContainSingle(source => source.States(Asv + "-BHSA"));
        StrongNumberSource.All.Should().ContainSingle(source => source.States(Pair));
        StrongNumberSource.All.Should().NotContain(source => source.States(Asv + "-TR1894"));
    }

    [Fact]
    public void A_content_word_s_number_naming_one_Greek_word_is_a_link_at_nine_tenths_credited_to_Maxfield()
    {
        var jesus = Rows().Single(row => row.From == "42.4:12.1");

        jesus.To.Should().Be("42.4:12.1");
        jesus.Confidence.Should().Be(StrongNumberMatch.Unambiguous);
        jesus.Evidence.Should().BeNull();
        jesus.Source.Should().Be(MaxfieldAsvEdition.Credit + ", matched within the verse against NESTLE1904");
    }

    [Fact]
    public void The_dropped_particle_reaches_nothing_from_the_word_it_sat_on() =>
        Rows().Should().NotContain(row => row.From.Contains("42.4:12.8"), "Luke 4:12's God carries no number once ὅτι's goes");

    /// <summary>Luke 4:13 in this copy writes θεός twice, and nothing says which one <em>God</em> renders (NOT-0228).</summary>
    [Fact]
    public void A_number_naming_two_Greek_words_of_the_verse_is_left_out()
    {
        var rows = Rows();

        rows.Should().NotContain(row => row.From == "42.4:13.1");
        rows.Single(row => row.From == "42.4:13.2").To.Should().Be("42.4:13.2");
    }

    [Fact]
    public void A_function_word_s_number_only_supports_and_a_content_word_s_does_not()
    {
        var rows = Rows();

        rows.Single(row => row.From == "42.4:12.3").Evidence.Should().Be(MappingMethods.SupportOnly, "that");
        rows.Single(row => row.From == "42.4:12.7").Evidence.Should().Be(MappingMethods.SupportOnly, "thy");
        rows.Single(row => row.From == "42.4:12.6").Evidence.Should().BeNull("Lord");
    }

    private List<MappingRow> Rows()
    {
        Text(Asv, "eng", "translation",
            [
                ("42.4:12", [("Jesus", "G2424"), ("said", "G2036"), ("that", "G3754"), ("but", "G1161"), ("the", null), ("Lord", "G2962"),
                    ("thy", "G4675"), ("God", null)]),
                ("42.4:13", [("God", "G2316"), ("spake", "G2980")]),
            ]);
        Text(NestleTextSource.Slug, "grc", "critical-edition",
            [
                ("42.4:12", [("Ἰησοῦς", "G2424"), ("εἶπεν", "G2036"), ("αὐτῷ", "G846"), ("ὅτι", "G3754"), ("δέ", "G1161"), ("κύριον", "G2962"),
                    ("τὸν", "G3588"), ("θεόν", "G2316"), ("σου", "G4675")]),
                ("42.4:13", [("θεός", "G2316"), ("ἐλάλησεν", "G2980"), ("θεοῦ", "G2316")]),
            ]);
        Directory.CreateDirectory(Path.Combine(_root, "Strong"));
        File.WriteAllText(Path.Combine(_root, StrongNumberSource.GreekDictionary), "<strongsdictionary></strongsdictionary>");
        return [.. StrongNumberSource.All.Single(source => source.States(Pair)).Rows(new ProductTexts(_store), Pair, _ => { })];
    }

    private void Text(string slug, string language, string kind, (string Verse, (string Surface, string? Strong)[] Words)[] verses)
    {
        using (var declared = _store.Create(TextField.ProductOf(slug), ProductKind.Hand, TextField.Schema, "test", []))
        {
            declared.WriteAll([new TextField("slug", slug), new TextField("kind", kind), new TextField("language", language),
                new TextField("versification", "kjv")]);
        }

        using (var writer = _store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", []))
        {
            writer.WriteAll(verses.OrderBy(verse => VerseKey.Parse(verse.Verse)).SelectMany(verse => verse.Words.Select((word, n) => new TextWord(
                new WordKey(slug, VerseKey.Parse(verse.Verse), n + 1), word.Surface, " ", null, word.Strong, null, null,
                null, null, false, null, null, null))));
        }

        using var frames = _store.Create(FrameFile.Product(slug), ProductKind.Machine, FrameRow.Schema, "test", []);
        frames.WriteAll(verses.Select(verse => new FrameRow(VerseKey.Parse(verse.Verse), null, [VerseKey.Parse(verse.Verse)], "test")));
    }

    /// <summary>A copy of the file's layout: every book's table, the rows given, and the <c>copyright</c> table.</summary>
    private static void Edition(string path, string terms, params (string Book, (int Chapter, int Verse, string Word, string Language, int Number)[] Rows)[] books)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        void Run(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            command.ExecuteNonQuery();
        }

        Run("CREATE TABLE copyright(CopyrightInfo text)");
        Run("INSERT INTO copyright VALUES ($terms)", ("$terms", terms));
        foreach (var book in Books)
        {
            Run($"CREATE TABLE \"{book}\" (Chapter integer KEY,Verse integer KEY,WordSeqNumber integer,Word varchar,pre varchar,post varchar,"
                + "WordFormat integer, OrigWordLanguage char(1), DictEntryNumber integer)");
        }

        foreach (var (book, rows) in books)
        {
            for (var at = 0; at < rows.Length; at++)
            {
                var row = rows[at];
                Run($"INSERT INTO \"{book}\" VALUES ($c, $v, $s, $w, '', '', 0, $l, $n)",
                    ("$c", row.Chapter), ("$v", row.Verse), ("$s", at), ("$w", row.Word), ("$l", row.Language), ("$n", row.Number));
            }
        }

        transaction.Commit();
    }
}
